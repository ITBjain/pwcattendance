using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using PwcApi.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    /// <summary>
    /// Coach → parents WhatsApp updates for a batch (daily activity photos + message).
    ///
    /// POST api/BatchMessaging/broadcast            → queues the job, returns immediately with broadcastId
    /// GET  api/BatchMessaging/broadcast/{id}       → live progress (sent / failed / pending per parent)
    /// GET  api/BatchMessaging/batch/{batchId}/history → previous updates sent for that batch
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BatchMessagingController : ControllerBase
    {
        private const int MaxImages = 6;
        private const int MaxImageBase64Chars = 6_000_000; // ≈ 4.5 MB per photo (app sends ~300 KB)

        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service;
        private readonly WhapiClient _whapi;
        private readonly ILogger<BatchMessagingController> _logger;

        public BatchMessagingController(AppDbContext context, R2StorageService r2Service,
                                        WhapiClient whapi, ILogger<BatchMessagingController> logger)
        {
            _context = context;
            _r2Service = r2Service;
            _whapi = whapi;
            _logger = logger;
        }

        private ObjectResult ApiError(int statusCode, string code, string message) =>
            StatusCode(statusCode, new { success = false, code, message });

        // =====================================================================
        // POST api/BatchMessaging/broadcast
        // =====================================================================
        [HttpPost("broadcast")]
        public async Task<IActionResult> CreateBroadcast([FromBody] BatchBroadcastRequest req)
        {
            try
            {
                if (req == null) return ApiError(400, "INVALID_PAYLOAD", "Invalid request.");
                if (!int.TryParse(req.CoachId, out int coachId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID. Please log out and log in again.");

                var message = req.Message?.Trim() ?? "";
                var images = (req.Images ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();

                if (message.Length == 0 && images.Count == 0)
                    return ApiError(400, "EMPTY_MESSAGE", "Write a message or add at least one photo.");
                if (images.Count > MaxImages)
                    return ApiError(400, "TOO_MANY_IMAGES", $"You can send up to {MaxImages} photos at a time.");
                if (images.Any(i => i.Length > MaxImageBase64Chars))
                    return ApiError(400, "IMAGE_TOO_LARGE", "One of the photos is too large. Please choose a smaller photo.");

                var coach = await _context.ResourceMasters.FirstOrDefaultAsync(r => r.Id == coachId);
                if (coach == null) return ApiError(404, "COACH_NOT_FOUND", "Your account was not found.");

                var batch = await _context.GroupVariations.FirstOrDefaultAsync(g => g.Id == req.BatchId);
                if (batch == null) return ApiError(404, "BATCH_NOT_FOUND", "This batch no longer exists.");

                if (_whapi.ResolveToken(coach) == null)
                    return ApiError(400, "WHATSAPP_NOT_CONNECTED",
                        "WhatsApp isn't connected for your account yet. Please ask the admin to link Whapi.");

                // ---- Recipients: paid enrollments of this batch (optionally a selected subset) ----
                var enrollments = await _context.ParentsEnrollments
                    .Where(pe => pe.GroupVariationId == req.BatchId && pe.PaymentStatus == "Paid")
                    .ToListAsync();

                var selected = req.EnrollmentIds ?? new List<int>();
                if (selected.Count > 0)
                    enrollments = enrollments.Where(e => selected.Contains(e.Id)).ToList();

                int skippedNoPhone = 0;
                var recipients = new List<BatchBroadcastRecipient>();
                // Siblings share a parent phone → one message per phone, children names combined
                foreach (var group in enrollments.GroupBy(e => PhoneUtil.ToWhatsAppNumber(e.ParentPhone)))
                {
                    if (group.Key == null) { skippedNoPhone += group.Count(); continue; }
                    var first = group.First();
                    var childNames = group.Select(g => g.ChildName?.Trim())
                                          .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                    recipients.Add(new BatchBroadcastRecipient
                    {
                        EnrollmentId = first.Id,
                        ParentName = first.ParentName,
                        ChildName = childNames.Count == 0 ? null : string.Join(" & ", childNames),
                        Phone = group.Key,
                        Status = "Pending"
                    });
                }

                if (recipients.Count == 0)
                    return ApiError(400, "NO_RECIPIENTS", skippedNoPhone > 0
                        ? "None of the selected parents have a valid WhatsApp number."
                        : "There are no enrolled (paid) parents in this batch to message.");

                // ---- Double-tap protection: same text to same batch in the last 2 minutes ----
                var now = IstClock.Now;
                var twoMinAgo = now.AddMinutes(-2);
                var duplicate = await _context.BatchBroadcasts
                    .Where(b => b.BatchId == req.BatchId && b.CoachId == coachId && b.CreatedAt >= twoMinAgo
                                && b.Message == message && b.Status != "Failed")
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync();
                if (duplicate != null)
                {
                    return Ok(new
                    {
                        success = true,
                        broadcastId = duplicate.Id,
                        totalRecipients = duplicate.TotalRecipients,
                        skippedNoPhone,
                        status = duplicate.Status,
                        message = "This update is already being sent."
                    });
                }

                // ---- Photos: data URIs for sending + permanent copy in R2 for history ----
                var payloads = new List<string>();
                var urls = new List<string>();
                for (int i = 0; i < images.Count; i++)
                {
                    var dataUri = WhapiClient.ToDataUri(images[i]);
                    if (dataUri == null)
                        return ApiError(400, "INVALID_IMAGE", $"Photo {i + 1} could not be read. Please pick it again.");
                    payloads.Add(dataUri);

                    try
                    {
                        var raw = dataUri.Substring(dataUri.IndexOf(',') + 1);
                        var url = await _r2Service.UploadBase64ImageAsync(raw, $"batch_{req.BatchId}_{DateTime.UtcNow.Ticks}_{i}");
                        if (!string.IsNullOrWhiteSpace(url)) urls.Add(url!);
                    }
                    catch (Exception ex)
                    {
                        // History copy only – sending still works without it
                        _logger.LogWarning(ex, "R2 upload failed for broadcast photo {Index}", i);
                    }
                }

                var broadcast = new BatchBroadcast
                {
                    BatchId = req.BatchId,
                    CoachId = coachId,
                    Message = message,
                    MediaUrls = JsonSerializer.Serialize(urls),
                    MediaPayloads = payloads.Count > 0 ? JsonSerializer.Serialize(payloads) : null,
                    Status = "Queued",
                    TotalRecipients = recipients.Count,
                    CreatedAt = now
                };
                _context.BatchBroadcasts.Add(broadcast);
                await _context.SaveChangesAsync();

                foreach (var r in recipients) r.BroadcastId = broadcast.Id;
                _context.BatchBroadcastRecipients.AddRange(recipients);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    broadcastId = broadcast.Id,
                    totalRecipients = recipients.Count,
                    skippedNoPhone,
                    status = broadcast.Status,
                    message = $"Sending to {recipients.Count} parent{(recipients.Count == 1 ? "" : "s")}…"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateBroadcast failed for Batch={BatchId}", req?.BatchId);
                return ApiError(500, "SERVER_ERROR", "Couldn't start sending. Please try again.");
            }
        }

        // =====================================================================
        // GET api/BatchMessaging/broadcast/{id}   (app polls this every ~2s)
        // =====================================================================
        [HttpGet("broadcast/{id:int}")]
        public async Task<IActionResult> GetBroadcast(int id)
        {
            try
            {
                var b = await _context.BatchBroadcasts.FirstOrDefaultAsync(x => x.Id == id);
                if (b == null) return ApiError(404, "NOT_FOUND", "Message job not found.");

                var recipients = await _context.BatchBroadcastRecipients
                    .Where(r => r.BroadcastId == id)
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.EnrollmentId, r.ParentName, r.ChildName, r.Phone, r.Status, r.Error })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    id = b.Id,
                    batchId = b.BatchId,
                    status = b.Status,
                    totalRecipients = b.TotalRecipients,
                    sentCount = b.SentCount,
                    failedCount = b.FailedCount,
                    pendingCount = recipients.Count(r => r.Status == "Pending"),
                    isFinished = b.Status == "Completed" || b.Status == "CompletedWithErrors" || b.Status == "Failed",
                    lastError = b.LastError,
                    createdAt = IstClock.ToIso(b.CreatedAt),
                    recipients = recipients.Select(r => new
                    {
                        enrollmentId = r.EnrollmentId,
                        parentName = r.ParentName,
                        childName = r.ChildName,
                        phone = PhoneUtil.Mask(r.Phone),
                        status = r.Status,
                        error = r.Error
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBroadcast failed for Id={Id}", id);
                return ApiError(500, "SERVER_ERROR", "Couldn't load sending progress.");
            }
        }

        // =====================================================================
        // GET api/BatchMessaging/batch/{batchId}/history
        // =====================================================================
        [HttpGet("batch/{batchId:int}/history")]
        public async Task<IActionResult> GetBatchHistory(int batchId, [FromQuery] int take = 20)
        {
            try
            {
                var rows = await _context.BatchBroadcasts
                    .Where(b => b.BatchId == batchId)
                    .OrderByDescending(b => b.Id)
                    .Take(Math.Clamp(take, 1, 100))
                    .ToListAsync();

                var data = rows.Select(b => new
                {
                    id = b.Id,
                    message = b.Message,
                    status = b.Status,
                    totalRecipients = b.TotalRecipients,
                    sentCount = b.SentCount,
                    failedCount = b.FailedCount,
                    createdAt = IstClock.ToIso(b.CreatedAt),
                    createdAtDisplay = b.CreatedAt.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture),
                    mediaUrls = SafeList(b.MediaUrls)
                });

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetBatchHistory failed for Batch={BatchId}", batchId);
                return ApiError(500, "SERVER_ERROR", "Couldn't load message history.");
            }
        }

        private static List<string> SafeList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch (JsonException) { return new List<string>(); }
        }
    }
}
