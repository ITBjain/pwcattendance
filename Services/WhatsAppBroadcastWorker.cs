using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PwcApi.Data;
using PwcApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PwcApi.Services
{
    /// <summary>
    /// Sends queued batch broadcasts (coach → parents) through Whapi, one parent at a time with a small
    /// random pause between parents (protects the WhatsApp number from spam flags).
    ///
    /// • Uses the database as the queue → survives Railway restarts/redeploys.
    /// • Each recipient row is marked Sent/Failed as it goes → nobody receives the same message twice.
    /// • Never crashes the API: every error is caught and logged.
    /// </summary>
    public class WhatsAppBroadcastWorker : BackgroundService
    {
        private const int WhatsAppCaptionLimit = 1000;

        private readonly IServiceScopeFactory _scopes;
        private readonly BroadcastSignal _signal;
        private readonly ILogger<WhatsAppBroadcastWorker> _logger;
        private readonly int _minDelayMs;
        private readonly int _maxDelayMs;

        public WhatsAppBroadcastWorker(IServiceScopeFactory scopes, BroadcastSignal signal,
                                       ILogger<WhatsAppBroadcastWorker> logger, IConfiguration config)
        {
            _scopes = scopes;
            _signal = signal;
            _logger = logger;
            // One shared number sends for every coach → keep a human-like pace (≈ 5–8 parents per minute).
            _minDelayMs = Math.Max(0, config.GetValue<int?>("Whapi:MinDelayMs") ?? 6000);
            _maxDelayMs = Math.Max(_minDelayMs, config.GetValue<int?>("Whapi:MaxDelayMs") ?? 12000);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await ResumeInterruptedJobsAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    bool processed = await ProcessNextBroadcastAsync(stoppingToken);
                    // Sleep until a coach queues something (instant wake-up) or 60 s pass (safety net)
                    if (!processed) await _signal.WaitAsync(TimeSpan.FromSeconds(60), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // e.g. tables not created yet – log and keep the API alive
                    _logger.LogError(ex, "WhatsAppBroadcastWorker loop error");
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>Jobs that were "Sending" when the server stopped go back to the queue (only Pending recipients are sent).</summary>
        private async Task ResumeInterruptedJobsAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stuck = await db.BatchBroadcasts.Where(b => b.Status == "Sending").ToListAsync(ct);
                foreach (var b in stuck) b.Status = "Queued";
                if (stuck.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("Re-queued {Count} interrupted broadcasts", stuck.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not resume interrupted broadcasts (have you run the SQL script?)");
            }
        }

        private async Task<bool> ProcessNextBroadcastAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var whapi = scope.ServiceProvider.GetRequiredService<WhapiClient>();

            var job = await db.BatchBroadcasts
                .Where(b => b.Status == "Queued")
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(ct);
            if (job == null) return false;

            job.Status = "Sending";
            job.StartedAt ??= IstClock.Now;
            await db.SaveChangesAsync(ct);

            var coach = await db.ResourceMasters.FirstOrDefaultAsync(r => r.Id == job.CoachId, ct);
            var token = whapi.ResolveBatchToken(coach); // the shared PWC WhatsApp number

            var media = new List<string>();
            if (!string.IsNullOrWhiteSpace(job.MediaPayloads))
            {
                try { media = JsonSerializer.Deserialize<List<string>>(job.MediaPayloads) ?? new List<string>(); }
                catch (JsonException) { media = new List<string>(); }
            }

            var pending = await db.BatchBroadcastRecipients
                .Where(r => r.BroadcastId == job.Id && r.Status == "Pending")
                .OrderBy(r => r.Id)
                .ToListAsync(ct);

            string? stopReason = token == null
                ? "The PWC WhatsApp number is not configured on the server (Whapi:DefaultToken is empty)."
                : null;

            var random = new Random();

            for (int i = 0; i < pending.Count; i++)
            {
                var recipient = pending[i];

                if (stopReason != null)
                {
                    recipient.Status = "Failed";
                    recipient.Error = stopReason;
                    job.FailedCount++;
                    continue;
                }

                var text = Personalize(job.Message, recipient);
                var result = await SendToRecipientAsync(whapi, token!, recipient.Phone, text, media, ct);

                if (result.Success)
                {
                    recipient.Status = "Sent";
                    recipient.SentAt = IstClock.Now;
                    recipient.Error = null;
                    job.SentCount++;
                }
                else
                {
                    recipient.Status = "Failed";
                    recipient.Error = result.Error;
                    job.FailedCount++;
                    job.LastError = result.Error;
                    if (result.IsAuthError)
                        stopReason = "WhatsApp rejected the Whapi token (disconnected or expired). Please reconnect WhatsApp.";
                }

                await db.SaveChangesAsync(ct); // progress is visible to the app immediately

                if (stopReason == null && i < pending.Count - 1)
                    await Task.Delay(random.Next(_minDelayMs, _maxDelayMs + 1), ct);
            }

            if (stopReason != null) job.LastError = stopReason;

            job.Status = job.FailedCount == 0
                ? "Completed"
                : (job.SentCount == 0 ? "Failed" : "CompletedWithErrors");
            job.CompletedAt = IstClock.Now;
            job.MediaPayloads = null; // free the database – permanent copies stay in MediaUrls (R2)
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Broadcast {Id} finished: {Sent} sent, {Failed} failed", job.Id, job.SentCount, job.FailedCount);
            return true;
        }

        private static async Task<WhapiResult> SendToRecipientAsync(WhapiClient whapi, string token, string phone,
                                                                    string text, List<string> media, CancellationToken ct)
        {
            if (media.Count == 0)
                return await whapi.SendTextAsync(token, phone, text, ct);

            bool hasText = !string.IsNullOrWhiteSpace(text);
            bool textAsCaption = hasText && text.Length <= WhatsAppCaptionLimit;

            // Long message → send it as its own text first, then the photos
            if (hasText && !textAsCaption)
            {
                var textResult = await whapi.SendTextAsync(token, phone, text, ct);
                if (!textResult.Success) return textResult;
                await Task.Delay(700, ct);
            }

            WhapiResult last = WhapiResult.Ok(200);
            for (int i = 0; i < media.Count; i++)
            {
                string? caption = (i == 0 && textAsCaption) ? text : null;
                last = await whapi.SendMediaAsync(token, "image", phone, media[i], caption, null, ct);
                if (!last.Success) return last;
                if (i < media.Count - 1) await Task.Delay(800, ct);
            }
            return last;
        }

        private static string Personalize(string? message, BatchBroadcastRecipient r)
        {
            if (string.IsNullOrWhiteSpace(message)) return "";
            var parent = string.IsNullOrWhiteSpace(r.ParentName) ? "Parent" : r.ParentName!.Trim();
            var child = string.IsNullOrWhiteSpace(r.ChildName) ? "your child" : r.ChildName!.Trim();
            return message
                .Replace("{parent}", parent, StringComparison.OrdinalIgnoreCase)
                .Replace("{child}", child, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }
    }
}
