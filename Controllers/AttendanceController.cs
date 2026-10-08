using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using PwcApi.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    /// <summary>
    /// Coach / counselor punch-in & punch-out.
    ///
    /// RULES (the database is the single source of truth):
    ///  • A person can have only ONE open session (CheckOutTime == null).
    ///  • GET status/{coachId} tells the app whether that session exists, so the app shows
    ///    "Punch Out" after being killed/reopened — even on the next day.
    ///  • A session left open from an earlier day must be punched out (with a reason) before a new punch-in.
    ///  • Every error returns { success:false, code, message } so the app can show a friendly message
    ///    instead of a raw "-1011" error.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service;
        private readonly ILogger<AttendanceController> _logger;
        private readonly IConfiguration _config;

        public AttendanceController(AppDbContext context, R2StorageService r2Service,
                                    ILogger<AttendanceController> logger, IConfiguration config)
        {
            _context = context;
            _r2Service = r2Service;
            _logger = logger;
            _config = config;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private ObjectResult ApiError(int statusCode, string code, string message) =>
            StatusCode(statusCode, new { success = false, code, message });

        private static DateTime StartOf(ResourceAttendance a) =>
            a.CheckInDate.Date + (a.CheckInTime ?? TimeSpan.Zero);

        private static DateTime? EndOf(ResourceAttendance a)
        {
            if (!a.CheckOutTime.HasValue) return null;
            var end = (a.CheckOutDate ?? a.CheckInDate).Date + a.CheckOutTime.Value;
            // Legacy rows (before CheckOutDate existed) that crossed midnight
            if (end < StartOf(a)) end = end.AddDays(1);
            return end;
        }

        private static string FormatDuration(TimeSpan d)
        {
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            return $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}";
        }

        private static string Fmt(DateTime dt, string format) => dt.ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Latest open session for this person (handles legacy duplicates safely).</summary>
        private Task<ResourceAttendance?> GetOpenSessionAsync(int resourceId) =>
            _context.ResourceAttendances
                .Where(a => a.ResourceId == resourceId && a.CheckOutTime == null)
                .OrderByDescending(a => a.CheckInDate)
                .ThenByDescending(a => a.CheckInTime)
                .ThenByDescending(a => a.Id)
                .FirstOrDefaultAsync();

        private async Task<AttendanceSessionDto> BuildSessionDtoAsync(ResourceAttendance a)
        {
            string? schoolName = null;
            if (!string.IsNullOrWhiteSpace(a.SchoolId))
            {
                schoolName = await _context.SchoolMaster
                    .Where(s => s.SchoolId == a.SchoolId)
                    .Select(s => s.SchoolName)
                    .FirstOrDefaultAsync();
            }

            var now = IstClock.Now;
            var start = StartOf(a);

            return new AttendanceSessionDto
            {
                RecordId = a.Id,
                Type = a.Type,
                SchoolId = a.SchoolId,
                SchoolName = schoolName,
                CheckInDate = Fmt(a.CheckInDate, "yyyy-MM-dd"),
                CheckInTime = (a.CheckInTime ?? TimeSpan.Zero).ToString(@"hh\:mm\:ss"),
                CheckInAt = IstClock.ToIso(start),
                ElapsedSeconds = Math.Max(0, (long)(now - start).TotalSeconds),
                IsPreviousDay = a.CheckInDate.Date < now.Date,
                TotalCalls = a.TotalCalls,
                TotalEmails = a.TotalEmails,
                TotalWhatsApp = a.TotalWhatsApp,
                TotalParentsTargeted = a.TotalParentsTargeted
            };
        }

        /// <summary>Strips any "data:image/jpeg;base64," prefix and uploads to R2. Returns null when no image was sent.</summary>
        private async Task<string?> UploadSelfieAsync(string? base64, string prefix, string coachId)
        {
            var clean = base64 ?? "";
            var comma = clean.IndexOf(',');
            if (comma >= 0) clean = clean.Substring(comma + 1);
            if (string.IsNullOrWhiteSpace(clean)) return null;

            return await _r2Service.UploadBase64ImageAsync(clean, $"{prefix}_{coachId}_{DateTime.UtcNow.Ticks}");
        }

        // =====================================================================
        // GET api/Attendance/status/{coachId}
        // Called by the app on launch, on resume and when the Attendance screen opens.
        // =====================================================================
        [HttpGet("status/{coachId}")]
        public async Task<IActionResult> GetStatus(string coachId)
        {
            try
            {
                if (!int.TryParse(coachId, out int resourceId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID.");

                var open = await GetOpenSessionAsync(resourceId);
                var serverTime = IstClock.ToIso(IstClock.Now);

                if (open == null)
                    return Ok(new { success = true, isCheckedIn = false, session = (AttendanceSessionDto?)null, serverTime });

                var dto = await BuildSessionDtoAsync(open);
                return Ok(new { success = true, isCheckedIn = true, session = dto, serverTime });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStatus failed for CoachId={CoachId}", coachId);
                return ApiError(500, "SERVER_ERROR", "Couldn't load your attendance status. Please try again.");
            }
        }

        // =====================================================================
        // POST api/Attendance/checkin
        // =====================================================================
        [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
        {
            try
            {
                if (request == null) return ApiError(400, "INVALID_PAYLOAD", "Invalid request.");

                if (!int.TryParse(request.CoachId, out int resourceId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID. Please log out and log in again.");

                var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceId);
                if (!resourceExists)
                    return ApiError(404, "COACH_NOT_FOUND", "Your account was not found. Please log out and log in again.");

                var type = string.IsNullOrWhiteSpace(request.Type) ? "InCentre" : request.Type.Trim();
                var schoolId = request.SchoolId?.Trim();
                bool needsCentre = type == "InSession" || type == "InCentre";
                if (needsCentre && string.IsNullOrWhiteSpace(schoolId))
                    return ApiError(400, "CENTRE_REQUIRED", "Please select a centre before punching in.");

                // 🔒 One open session at a time. Return it so the app can switch to "Punch Out" immediately.
                var open = await GetOpenSessionAsync(resourceId);
                if (open != null)
                {
                    var dto = await BuildSessionDtoAsync(open);
                    var start = StartOf(open);
                    var since = dto.IsPreviousDay
                        ? $"{Fmt(start, "ddd, dd MMM")} at {Fmt(start, "hh:mm tt")}"
                        : Fmt(start, "hh:mm tt");

                    return StatusCode(409, new
                    {
                        success = false,
                        code = "ALREADY_CHECKED_IN",
                        message = $"You are already punched in since {since}. Please punch out first.",
                        session = dto
                    });
                }

                string? imageUrl = await UploadSelfieAsync(request.CheckInImage, "checkin", request.CoachId);

                var now = IstClock.Now;
                var record = new ResourceAttendance
                {
                    ResourceId = resourceId,
                    CheckInDate = now.Date,
                    CheckInTime = now.TimeOfDay,
                    SchoolId = !string.IsNullOrWhiteSpace(schoolId)
                        ? schoolId!
                        : (type == "InOffice" ? "InOffice" : "InTraining"),
                    CheckInImage = imageUrl ?? "",
                    CheckInLocation = request.CheckInLocation ?? "",
                    Type = type,
                    AttendanceRemark = request.AttendanceRemark ?? "",
                    TotalCalls = 0,
                    TotalEmails = 0,
                    TotalWhatsApp = 0,
                    TotalParentsTargeted = 0
                };

                _context.ResourceAttendances.Add(record);
                await _context.SaveChangesAsync();

                var session = await BuildSessionDtoAsync(record);
                return Ok(new
                {
                    success = true,
                    message = "Punched in successfully",
                    recordId = record.Id,
                    session
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckIn failed for CoachId={CoachId}", request?.CoachId);
                return ApiError(500, "SERVER_ERROR", "Punch-in could not be saved. Please try again in a moment.");
            }
        }

        // =====================================================================
        // PUT api/Attendance/checkout
        // =====================================================================
        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
        {
            try
            {
                if (request == null) return ApiError(400, "INVALID_PAYLOAD", "Invalid request.");

                if (!int.TryParse(request.CoachId, out int resourceId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID. Please log out and log in again.");

                var open = await GetOpenSessionAsync(resourceId);
                if (open == null)
                    return ApiError(404, "NO_ACTIVE_SESSION", "You are not punched in right now — your last session is already closed.");

                var now = IstClock.Now;
                bool isPreviousDay = open.CheckInDate.Date < now.Date;
                var remark = request.Remark?.Trim();

                // Never lose activity counted by sync-activity (the app may have been reinstalled/reset)
                int calls = Math.Max(request.TotalCalls, open.TotalCalls);
                int emails = Math.Max(request.TotalEmails, open.TotalEmails);
                int whatsApp = Math.Max(request.TotalWhatsApp, open.TotalWhatsApp);
                int targeted = Math.Max(request.TotalParentsTargeted, open.TotalParentsTargeted);

                if (isPreviousDay && string.IsNullOrWhiteSpace(remark))
                {
                    var start = StartOf(open);
                    return ApiError(400, "REMARK_REQUIRED",
                        $"You didn't punch out on {Fmt(start, "ddd, dd MMM")}. Please add a short reason to close that session.");
                }

                // Optional business rule (off by default): counselors must explain a zero-activity day.
                bool requireRemarkOnNoActivity = _config.GetValue<bool>("Attendance:RequireRemarkWhenNoActivity");
                if (requireRemarkOnNoActivity && calls + emails + whatsApp == 0 && string.IsNullOrWhiteSpace(remark))
                {
                    var role = await _context.ResourceMasters
                        .Where(r => r.Id == resourceId).Select(r => r.Role).FirstOrDefaultAsync();
                    if (string.Equals(role, "Counselor", StringComparison.OrdinalIgnoreCase))
                        return ApiError(400, "REMARK_REQUIRED",
                            "No calls, WhatsApp messages or emails were logged in this session. Please add a remark to punch out.");
                }

                string? imageUrl = await UploadSelfieAsync(request.CheckOutImage, "checkout", request.CoachId);

                open.CheckOutTime = now.TimeOfDay;
                open.CheckOutDate = now.Date;
                if (imageUrl != null) open.CheckOutImage = imageUrl;
                open.CheckOutLocation = request.CheckOutLocation ?? "";
                open.TotalCalls = calls;
                open.TotalEmails = emails;
                open.TotalWhatsApp = whatsApp;
                open.TotalParentsTargeted = targeted;
                open.Remark = remark;

                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(open.AttendanceRemark)) notes.Add(open.AttendanceRemark!);

                if (request.Type == "OutOfRange")
                {
                    open.Type = "OutOfRange";
                    notes.Add("CheckOut: " + (string.IsNullOrWhiteSpace(request.AttendanceRemark)
                        ? "Out of range" : request.AttendanceRemark));
                }
                if (isPreviousDay)
                {
                    notes.Add($"Late punch-out on {Fmt(now, "dd MMM yyyy hh:mm tt")} for session of {Fmt(open.CheckInDate, "dd MMM yyyy")}");
                }
                open.AttendanceRemark = string.Join(" | ", notes);

                // Safety net: close any legacy duplicate open sessions so the person can never get stuck.
                var orphans = await _context.ResourceAttendances
                    .Where(a => a.ResourceId == resourceId && a.CheckOutTime == null && a.Id != open.Id)
                    .ToListAsync();
                foreach (var o in orphans)
                {
                    o.CheckOutTime = o.CheckInTime ?? TimeSpan.Zero;
                    o.CheckOutDate = o.CheckInDate.Date;
                    o.AttendanceRemark = string.IsNullOrWhiteSpace(o.AttendanceRemark)
                        ? "Auto-closed: duplicate open session"
                        : o.AttendanceRemark + " | Auto-closed: duplicate open session";
                }

                await _context.SaveChangesAsync();

                var startAt = StartOf(open);
                var endAt = EndOf(open) ?? now;
                var duration = endAt - startAt;

                return Ok(new
                {
                    success = true,
                    message = isPreviousDay
                        ? "Previous session closed. You can punch in for today now."
                        : "Punched out successfully",
                    recordId = open.Id,
                    checkInAt = IstClock.ToIso(startAt),
                    checkOutAt = IstClock.ToIso(endAt),
                    durationSeconds = (long)Math.Max(0, duration.TotalSeconds),
                    duration = FormatDuration(duration),
                    wasPreviousDay = isPreviousDay,
                    totalCalls = calls,
                    totalEmails = emails,
                    totalWhatsApp = whatsApp
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckOut failed for CoachId={CoachId}", request?.CoachId);
                return ApiError(500, "SERVER_ERROR", "Punch-out could not be saved. Please try again in a moment.");
            }
        }

        // =====================================================================
        // POST api/Attendance/sync-activity  (live call/WhatsApp/email counters)
        // =====================================================================
        [HttpPost("sync-activity")]
        public async Task<IActionResult> SyncActivity([FromBody] SyncActivityRequest request)
        {
            try
            {
                if (request == null || !int.TryParse(request.CoachId, out int resourceId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID.");

                var open = await GetOpenSessionAsync(resourceId);
                if (open == null)
                    return ApiError(404, "NO_ACTIVE_SESSION", "No active session found. Please punch in first.");

                // Counters only ever go up – a reset phone can never wipe the day's totals.
                open.TotalCalls = Math.Max(open.TotalCalls, request.TotalCalls);
                open.TotalEmails = Math.Max(open.TotalEmails, request.TotalEmails);
                open.TotalWhatsApp = Math.Max(open.TotalWhatsApp, request.TotalWhatsApp);
                open.TotalParentsTargeted = Math.Max(open.TotalParentsTargeted, request.TotalParentsTargeted);

                await _context.SaveChangesAsync();
                return Ok(new
                {
                    success = true,
                    totalCalls = open.TotalCalls,
                    totalEmails = open.TotalEmails,
                    totalWhatsApp = open.TotalWhatsApp,
                    totalParentsTargeted = open.TotalParentsTargeted
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncActivity failed for CoachId={CoachId}", request?.CoachId);
                return ApiError(500, "SERVER_ERROR", "Activity sync failed.");
            }
        }

        // =====================================================================
        // GET api/Attendance/reports/{coachId}?days=60   (attendance history)
        // coachId = ResourceMasters.Id (same id used for check-in), NOT EmpId.
        // =====================================================================
        [HttpGet("reports/{coachId}")]
        public async Task<IActionResult> GetReports(string coachId, [FromQuery] int days = 60)
        {
            try
            {
                if (!int.TryParse(coachId, out int resourceId))
                    return ApiError(400, "INVALID_COACH_ID", "Invalid coach ID format.");

                var from = IstClock.Today.AddDays(-Math.Clamp(days, 1, 366));

                var rows = await _context.ResourceAttendances
                    .Where(a => a.ResourceId == resourceId && a.CheckInDate >= from)
                    .OrderByDescending(a => a.CheckInDate)
                    .ThenByDescending(a => a.CheckInTime)
                    .ToListAsync();

                var schoolIds = rows.Select(r => r.SchoolId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
                var schoolNames = await _context.SchoolMaster
                    .Where(s => schoolIds.Contains(s.SchoolId))
                    .Select(s => new { s.SchoolId, s.SchoolName })
                    .ToListAsync();
                var nameById = schoolNames.ToDictionary(s => s.SchoolId, s => s.SchoolName);

                var now = IstClock.Now;
                var reports = rows.Select(a =>
                {
                    var start = StartOf(a);
                    var end = EndOf(a);
                    var duration = (end ?? now) - start;
                    nameById.TryGetValue(a.SchoolId ?? "", out var schoolName);

                    return new
                    {
                        sessionId = a.Id.ToString(),
                        date = Fmt(a.CheckInDate, "dd MMM yyyy"),
                        dateIso = Fmt(a.CheckInDate, "yyyy-MM-dd"),
                        checkInTime = a.CheckInTime.HasValue ? $"{a.CheckInTime.Value.Hours:D2}:{a.CheckInTime.Value.Minutes:D2}" : "--:--",
                        checkOutTime = a.CheckOutTime.HasValue ? $"{a.CheckOutTime.Value.Hours:D2}:{a.CheckOutTime.Value.Minutes:D2}" : "--:--",
                        checkOutDate = end.HasValue ? Fmt(end.Value, "yyyy-MM-dd") : null,
                        duration = FormatDuration(duration),
                        durationSeconds = (long)Math.Max(0, duration.TotalSeconds),
                        status = a.CheckOutTime == null ? "Active" : "Completed",
                        calls = a.TotalCalls,
                        emails = a.TotalEmails,
                        whatsapp = a.TotalWhatsApp,
                        targeted = a.TotalParentsTargeted,
                        remark = a.Remark,
                        type = a.Type ?? "",
                        attendanceRemark = a.AttendanceRemark,
                        schoolId = a.SchoolId,
                        schoolName
                    };
                }).ToList();

                return Ok(new { success = true, data = reports });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetReports failed for CoachId={CoachId}", coachId);
                return ApiError(500, "SERVER_ERROR", "Couldn't load attendance history.");
            }
        }

        // =====================================================================
        // INDIVIDUAL PARENT INTERACTION TRACKING (unchanged)
        // =====================================================================

        [HttpPost("log-interaction")]
        public async Task<IActionResult> LogInteraction([FromBody] LogInteractionRequest request)
        {
            try
            {
                var log = new InteractionLog
                {
                    ResourceId = request.ResourceId,
                    ParentId = request.ParentId,
                    InteractionType = request.InteractionType,
                    Status = request.Status,
                    DurationSeconds = request.DurationSeconds,
                    CreatedAt = IstClock.Now
                };

                _context.InteractionLogs.Add(log);
                await _context.SaveChangesAsync();

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LogInteraction failed");
                return ApiError(500, "SERVER_ERROR", ex.Message);
            }
        }

        [HttpGet("parent-history/{parentId}")]
        public async Task<IActionResult> GetParentHistory(int parentId)
        {
            try
            {
                var history = await _context.InteractionLogs
                    .Where(log => log.ParentId == parentId)
                    .OrderByDescending(log => log.CreatedAt)
                    .Select(log => new
                    {
                        interactionType = log.InteractionType,
                        status = log.Status,
                        durationSeconds = log.DurationSeconds,
                        date = log.CreatedAt.ToString("dd MMM yyyy, hh:mm tt")
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = history });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetParentHistory failed for ParentId={ParentId}", parentId);
                return ApiError(500, "SERVER_ERROR", ex.Message);
            }
        }

        // =====================================================================
        // BUSINESS CENTRAL SYNC (unchanged format + CheckOutDate added)
        // =====================================================================
        [HttpGet("bc-sync/{empId}")]
        public async Task<IActionResult> GetAttendanceForBusinessCentral(string empId)
        {
            try
            {
                _logger.LogInformation("BC Sync requested for EmpId={EmpId}", empId);
                var resource = await _context.ResourceMasters.FirstOrDefaultAsync(r => r.EmpId == empId);
                if (resource == null)
                {
                    _logger.LogWarning("BC Sync missing resource for EmpId={EmpId}", empId);
                    return NotFound(new { message = $"No counselor found with EmpId: {empId}" });
                }

                var attendances = await _context.ResourceAttendances
                    .Where(a => a.ResourceId == resource.Id)
                    .OrderByDescending(a => a.CheckInDate)
                    .ToListAsync();

                var bcPayload = attendances.Select(a => new
                {
                    Id = a.Id,
                    ResourceId = a.ResourceId,
                    EmpId = resource.EmpId,
                    SchoolId = a.SchoolId,
                    SessionId = a.SessionId,
                    Date = a.CheckInDate.ToString("dd-MM-yyyy"),
                    CheckInTime = a.CheckInTime.HasValue ? $"{a.CheckInTime.Value.Hours}.{a.CheckInTime.Value.Minutes:D2}" : "",
                    CheckOutTime = a.CheckOutTime.HasValue ? $"{a.CheckOutTime.Value.Hours}.{a.CheckOutTime.Value.Minutes:D2}" : "",
                    CheckOutDate = a.CheckOutDate.HasValue ? a.CheckOutDate.Value.ToString("dd-MM-yyyy") : "",
                    CheckInImage = a.CheckInImage ?? "",
                    CheckOutImage = a.CheckOutImage ?? "",
                    CheckInLocation = a.CheckInLocation ?? "",
                    CheckOutLocation = a.CheckOutLocation ?? "",
                    Type = a.Type ?? "",
                    AttendanceRemark = a.AttendanceRemark ?? "",
                    CreatedAt = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                });

                return Ok(bcPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAttendanceForBusinessCentral failed for EmpId={EmpId}", empId);
                return StatusCode(500, new { message = $"BC Sync Error: {ex.Message}" });
            }
        }
    }
}



// using Microsoft.AspNetCore.Mvc;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using PwcApi.Data;
// using PwcApi.DTOs;
// using PwcApi.Models;
// using PwcApi.Services;
// using System;
// using System.Linq;
// using System.Threading.Tasks;

// namespace PwcApi.Controllers
// {
//     [ApiController]
//     [Route("api/[controller]")]
//     public class AttendanceController : ControllerBase
//     {
//         private readonly AppDbContext _context;
//         private readonly R2StorageService _r2Service;
//         private readonly ILogger<AttendanceController> _logger;

//         public AttendanceController(AppDbContext context, R2StorageService r2Service, ILogger<AttendanceController> logger)
//         {
//             _context = context;
//             _r2Service = r2Service;
//             _logger = logger;
//         }

//         // 🔥 HELPER FUNCTION: Safely get IST on both Windows and Linux servers
//         private DateTime GetIndiaTime()
//         {
//             try 
//             {
//                 return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
//             }
//             catch 
//             {
//                 return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
//             }
//         }

// [HttpPost("checkin")]
// public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
// {
//     try 
//     {
//         if (request == null) return BadRequest(new { message = "Invalid payload" });

//         if (!int.TryParse(request.CoachId, out int resourceIdInt))
//             return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

//         var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceIdInt);
//         if (!resourceExists) return NotFound(new { message = $"Counselor with ID {request.CoachId} does not exist." });

//         var existingSession = await _context.ResourceAttendances
//             .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

//         if (existingSession != null) return Conflict(new { message = "You are already checked in. Please check out first." });

//         string cleanBase64 = request.CheckInImage ?? "";
//         if (cleanBase64.Contains(",")) {
//             cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
//         }

//         // If it's a WorkFromHome auto punch-in or similar, image might be empty
//         string? imageUrl = null;
//         if (!string.IsNullOrWhiteSpace(cleanBase64)) 
//         {
//             imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkin_{request.CoachId}_{DateTime.UtcNow.Ticks}");
//         }

//         // Use IST Time instead of UtcNow
//         var indiaTime = GetIndiaTime();

//         var attendanceRecord = new ResourceAttendance
//         {
//             ResourceId = resourceIdInt,   
//             CheckInTime = indiaTime.TimeOfDay,                   
//             CheckInDate = indiaTime.Date,

//             // 🔥 FIX: Pass an empty string "" instead of null to prevent the MySQL crash!
//             SchoolId = string.IsNullOrWhiteSpace(request.SchoolId) ? "InTraining" : request.SchoolId,

//             CheckInImage = imageUrl ?? "",
//             CheckInLocation = request.CheckInLocation ?? "", 
            
//             Type = request.Type ?? "InCentre",
//             AttendanceRemark = request.AttendanceRemark ?? "",

//             TotalCalls = 0,
//             TotalEmails = 0,
//             TotalWhatsApp = 0,
//             TotalParentsTargeted = 0
//         };

//         _context.ResourceAttendances.Add(attendanceRecord);
//         await _context.SaveChangesAsync();

//         return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
//     }
//     catch (Exception ex)
//     {
//         _logger.LogError(ex, "CheckIn failed for CoachId={CoachId}", request?.CoachId);
//         var actualError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
//         return StatusCode(500, new { message = $"Backend Crash: {actualError}" });
//     }
// }
// //         [HttpPost("checkin")]
// //         public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
// //         {
// //             try 
// //             {
// //                 if (request == null) return BadRequest(new { message = "Invalid payload" });

// //                 if (!int.TryParse(request.CoachId, out int resourceIdInt))
// //                     return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

// //                 var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceIdInt);
// //                 if (!resourceExists) return NotFound(new { message = $"Counselor with ID {request.CoachId} does not exist." });

// //                 var existingSession = await _context.ResourceAttendances
// //                     .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

// //                 if (existingSession != null) return Conflict(new { message = "You are already checked in. Please check out first." });

// //                 string cleanBase64 = request.CheckInImage ?? "";
// //                 if (cleanBase64.Contains(",")) {
// //                     cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
// //                 }

// //                 // If it's a WorkFromHome auto punch-in or similar, image might be empty
// //                 string? imageUrl = null;
// //                 if (!string.IsNullOrWhiteSpace(cleanBase64)) 
// //                 {
// //                     imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkin_{request.CoachId}_{DateTime.UtcNow.Ticks}");
// //                 }

// //                 // Use IST Time instead of UtcNow
// //                 var indiaTime = GetIndiaTime();

// //                 var attendanceRecord = new ResourceAttendance
// //                 {
// //                     ResourceId = resourceIdInt,   
// //                     CheckInTime = indiaTime.TimeOfDay,                   
// //                       SchoolId = string.IsNullOrWhiteSpace(request.SchoolId) ? null : request.SchoolId, // 🔥 Force it to null if empty                    CheckInDate = indiaTime.Date,

// //                     CheckInImage = imageUrl ?? "",
// //                     CheckInLocation = request.CheckInLocation,
                    
// //                     // 🔥 NEW: Store Attendance Type and Remark
// //                     Type = request.Type ?? "InCentre",
// //                     AttendanceRemark = request.AttendanceRemark,

// //                     TotalCalls = 0,
// //                     TotalEmails = 0,
// //                     TotalWhatsApp = 0,
// //                     TotalParentsTargeted = 0
// //                 };

// //                 _context.ResourceAttendances.Add(attendanceRecord);
// //                 await _context.SaveChangesAsync();

// //                 return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
// //             }
// //             // 👉 This is C# code. Put this in your .NET Backend!
// // catch (Exception ex)
// // {
// //     var actualError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
// //     return StatusCode(500, new { message = $"Backend Crash: {actualError}" });
// // }
// //         }

//         [HttpPost("sync-activity")]
//         public async Task<IActionResult> SyncActivity([FromBody] SyncActivityRequest request)
//         {
//             if (!int.TryParse(request.CoachId, out int resourceIdInt))
//                 return BadRequest(new { message = "Invalid Coach ID." });

//             var activeSession = await _context.ResourceAttendances
//                 .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

//             if (activeSession == null)
//                 return NotFound(new { message = "No active session found. Must check-in first." });

//             activeSession.TotalCalls = request.TotalCalls;
//             activeSession.TotalEmails = request.TotalEmails;
//             activeSession.TotalWhatsApp = request.TotalWhatsApp;
//             activeSession.TotalParentsTargeted = request.TotalParentsTargeted;

//             _context.ResourceAttendances.Update(activeSession);
//             await _context.SaveChangesAsync();

//             return Ok(new { success = true });
//         }

//         [HttpGet("reports/{coachId}")]
//         public async Task<IActionResult> GetReports(string coachId)
//         {
//             if (!int.TryParse(coachId, out int resourceIdInt))
//                 return BadRequest(new { message = "Invalid Coach ID format." });

//             var reports = await _context.ResourceAttendances
//                 .Where(a => a.ResourceId == resourceIdInt)
//                 .OrderByDescending(a => a.CheckInDate).ThenByDescending(a => a.CheckInTime)
//                 .Select(a => new {
//                     sessionId = a.Id.ToString(),
//                     date = a.CheckInDate.ToString("dd MMM yyyy"),
//                     checkInTime = a.CheckInTime.HasValue ? $"{a.CheckInTime.Value.Hours:D2}:{a.CheckInTime.Value.Minutes:D2}" : "--:--",
//                     checkOutTime = a.CheckOutTime.HasValue ? $"{a.CheckOutTime.Value.Hours:D2}:{a.CheckOutTime.Value.Minutes:D2}" : "--:--",
//                     status = a.CheckOutTime == null ? "Active" : "Completed",
//                     calls = a.TotalCalls,
//                     emails = a.TotalEmails,
//                     whatsapp = a.TotalWhatsApp,
//                     targeted = a.TotalParentsTargeted,
//                     remark = a.Remark,
//                     type = a.Type,                        // 🔥 Pass Type back to UI
//                     attendanceRemark = a.AttendanceRemark // 🔥 Pass Remark back to UI
//                 })
//                 .ToListAsync();

//             return Ok(new { success = true, data = reports });
//         }

//         [HttpPut("checkout")]
//         public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
//         {
//             try
//             {
//                 if (request == null) return BadRequest(new { message = "Invalid payload" });

//                 if (!int.TryParse(request.CoachId, out int resourceIdInt))
//                     return BadRequest(new { message = "Invalid Coach ID format." });

//                 var attendanceRecord = await _context.ResourceAttendances
//                     .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

//                 if (attendanceRecord == null) return NotFound(new { message = "No active check-in found to check out from." });

//                 var totalActivity = request.TotalCalls + request.TotalEmails + request.TotalWhatsApp;
//                 if (totalActivity == 100 && string.IsNullOrWhiteSpace(request.Remark))
//                 {
//                     return BadRequest(new { message = "Activity is 0. A remark is required to check out." });
//                 }

//                 string cleanBase64 = request.CheckOutImage ?? "";
//                 if (cleanBase64.Contains(",")) {
//                     cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
//                 }

//                 string? imageUrl = null;
//                 if (!string.IsNullOrWhiteSpace(cleanBase64)) 
//                 {
//                     imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkout_{request.CoachId}_{DateTime.UtcNow.Ticks}");
//                 }

//                 // Use IST Time
//                 var indiaTime = GetIndiaTime();

//                 attendanceRecord.CheckOutTime = indiaTime.TimeOfDay;
                
//                 if (imageUrl != null) {
//                     attendanceRecord.CheckOutImage = imageUrl; 
//                 }
                
//                 attendanceRecord.CheckOutLocation = request.CheckOutLocation;
//                 attendanceRecord.TotalCalls = request.TotalCalls;
//                 attendanceRecord.TotalEmails = request.TotalEmails;
//                 attendanceRecord.TotalWhatsApp = request.TotalWhatsApp;
//                 attendanceRecord.TotalParentsTargeted = request.TotalParentsTargeted;
//                 attendanceRecord.Remark = request.Remark;

//                 // 🔥 NEW: Handle Geofence OutOfRange Logic for Checkout
//                 if (!string.IsNullOrEmpty(request.Type) && request.Type == "OutOfRange")
//                 {
//                     attendanceRecord.Type = "OutOfRange";
//                     attendanceRecord.AttendanceRemark = string.IsNullOrWhiteSpace(attendanceRecord.AttendanceRemark) 
//                         ? request.AttendanceRemark 
//                         : attendanceRecord.AttendanceRemark + " | CheckOut: " + request.AttendanceRemark;
//                 }

//                 _context.ResourceAttendances.Update(attendanceRecord);
//                 await _context.SaveChangesAsync();

//                 return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { message = $"Backend Crash: {ex.Message}" });
//             }
//         }



//         // ===============================================================
//         // 🔥 NEW ENDPOINTS: INDIVIDUAL PARENT INTERACTION TRACKING
//         // ===============================================================

//         [HttpPost("log-interaction")]
//         public async Task<IActionResult> LogInteraction([FromBody] LogInteractionRequest request)
//         {
//             try
//             {
//                 var log = new InteractionLog
//                 {
//                     ResourceId = request.ResourceId,
//                     ParentId = request.ParentId,
//                     InteractionType = request.InteractionType,
//                     Status = request.Status,
//                     DurationSeconds = request.DurationSeconds,
//                     CreatedAt = GetIndiaTime() // Stores precise local time
//                 };

//                 _context.InteractionLogs.Add(log);
//                 await _context.SaveChangesAsync();

//                 return Ok(new { success = true });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { message = ex.Message });
//             }
//         }

//         [HttpGet("parent-history/{parentId}")]
//         public async Task<IActionResult> GetParentHistory(int parentId)
//         {
//             try
//             {
//                 var history = await _context.InteractionLogs
//                     .Where(log => log.ParentId == parentId)
//                     .OrderByDescending(log => log.CreatedAt)
//                     .Select(log => new {
//                         interactionType = log.InteractionType,
//                         status = log.Status,
//                         durationSeconds = log.DurationSeconds,
//                         date = log.CreatedAt.ToString("dd MMM yyyy, hh:mm tt")
//                     })
//                     .ToListAsync();

//                 return Ok(new { success = true, data = history });
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "GetParentHistory failed for ParentId={ParentId}", parentId);
//                 return StatusCode(500, new { message = ex.Message });
//             }
//         }

//         [HttpGet("bc-sync/{empId}")]
//         public async Task<IActionResult> GetAttendanceForBusinessCentral(string empId)
//         {
//             try
//             {
//                 _logger.LogInformation("BC Sync requested for EmpId={EmpId}", empId);
//                 var resource = await _context.ResourceMasters.FirstOrDefaultAsync(r => r.EmpId == empId);
//                 if (resource == null)
//                 {
//                     _logger.LogWarning("BC Sync missing resource for EmpId={EmpId}", empId);
//                     return NotFound(new { message = $"No counselor found with EmpId: {empId}" });
//                 }

//                 var attendances = await _context.ResourceAttendances
//                     .Where(a => a.ResourceId == resource.Id)
//                     .OrderByDescending(a => a.CheckInDate)
//                     .ToListAsync();

//                 var bcPayload = attendances.Select(a => new
//                 {
//                     Id = a.Id,
//                     ResourceId = a.ResourceId,
//                     EmpId = resource.EmpId,         
//                     SchoolId = a.SchoolId,
//                     SessionId = a.SessionId,
//                     Date = a.CheckInDate.ToString("dd-MM-yyyy"),
//                     CheckInTime = a.CheckInTime.HasValue ? $"{a.CheckInTime.Value.Hours}.{a.CheckInTime.Value.Minutes:D2}" : "",
//                     CheckOutTime = a.CheckOutTime.HasValue ? $"{a.CheckOutTime.Value.Hours}.{a.CheckOutTime.Value.Minutes:D2}" : "",
//                     CheckInImage = a.CheckInImage ?? "",
//                     CheckOutImage = a.CheckOutImage ?? "",
//                     CheckInLocation = a.CheckInLocation ?? "",
//                     CheckOutLocation = a.CheckOutLocation ?? "",
                    
//                     Type = a.Type ?? "",                         // 🔥 Added to BC Sync
//                     AttendanceRemark = a.AttendanceRemark ?? "", // 🔥 Added to BC Sync
                    
//                     CreatedAt = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
//                 });

//                 return Ok(bcPayload);
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "GetAttendanceForBusinessCentral failed for EmpId={EmpId}", empId);
//                 return StatusCode(500, new { message = $"BC Sync Error: {ex.Message}" });
//             }
//         }
//     }
//     }

// //         [HttpGet("coach/{coachId}/active-batches")]
// // public async Task<IActionResult> GetActiveBatches(string coachId)
// // {
// //     var today = DateTime.UtcNow.Date;

// //     // Get Active Sessions for this Coach
// //     var activeSessionIds = await _context.SessionMasters
// //         .Where(s => s.CoachId == coachId && s.IsActive == 1)
// //         .Select(s => s.Id)
// //         .ToListAsync();

// //     // Fetch Batches and include today's attendance status
// //     var batches = await _context.GroupVariations
// //         .Where(gv => activeSessionIds.Contains(gv.SessionId))
// //         .Select(gv => new
// //         {
// //             BatchId = gv.Id,
// //             AgeGroup = gv.AgeGroup,
// //             Timing = gv.TimeSlot,
// //             Days = gv.Days,
// //             Children = _context.ParentsEnrollments
// //                 .Where(pe => pe.GroupVariationId == gv.Id)
// //                 .Select(pe => new
// //                 {
// //                     EnrollmentId = pe.Id,
// //                     ChildName = pe.ChildName,
// //                     ParentPhone = pe.ParentPhone,
// //                     ParentEmail = pe.ParentEmail,
// //                     // Check if they are already marked present today
// //                     TodayAttendance = _context.ChildAttendances
// //                         .FirstOrDefault(a => a.ChildEnrollmentId == pe.Id && a.AttendanceDate == today)
// //                 }).ToList()
// //         }).ToListAsync();

// //     return Ok(batches);
// // }
// //     }
// // }

// // using Microsoft.AspNetCore.Mvc;
// // using Microsoft.EntityFrameworkCore;
// // using PwcApi.Data;
// // using PwcApi.DTOs;
// // using PwcApi.Models;
// // using PwcApi.Services;
// // using System;
// // using System.Threading.Tasks;

// // namespace PwcApi.Controllers
// // {
// //     [ApiController]
// //     [Route("api/[controller]")]
// //     public class AttendanceController : ControllerBase
// //     {
// //         private readonly AppDbContext _context;
// //         private readonly R2StorageService _r2Service;

// //         public AttendanceController(AppDbContext context, R2StorageService r2Service)
// //         {
// //             _context = context;
// //             _r2Service = r2Service;
// //         }

// //        [HttpPost("checkin")]
// //         public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
// //         {
// //             try 
// //             {
// //                 if (request == null) return BadRequest(new { message = "Invalid payload" });

// //                 if (!int.TryParse(request.CoachId, out int resourceIdInt))
// //                     return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

// //                 // 1. Verify the user exists
// //                 var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceIdInt);
// //                 if (!resourceExists) return NotFound(new { message = $"Counselor with ID {request.CoachId} does not exist." });

// //                 // 2. Check if they are already checked in
// //                 var existingSession = await _context.ResourceAttendances
// //                     .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

// //                 if (existingSession != null) return Conflict(new { message = "You are already checked in. Please check out first." });

// //                 // 🔥 FIX 1: BULLETPROOF BASE64 CLEANER
// //                 // This ensures _r2Service never crashes regardless of what Android sends
// //                 string cleanBase64 = request.CheckInImage ?? "";
// //                 if (cleanBase64.Contains(",")) {
// //                     cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
// //                 }

// //                 // Upload Image (Added a timestamp to prevent duplicate filename overwrites)
// //                 string? imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkin_{request.CoachId}_{DateTime.UtcNow.Ticks}");

// //                 var currentTime = DateTime.UtcNow;

// //                 // 3. Save to Database
// //                 var attendanceRecord = new ResourceAttendance
// //                 {
// //                     ResourceId = resourceIdInt,   
// //                     SchoolId = request.SchoolId,  
// //                     CheckInDate = currentTime.Date,
// //                     CheckInTime = currentTime.TimeOfDay, 
// //                     CheckInImage = imageUrl, 
// //                     CheckInLocation = request.CheckInLocation
// //                 };

// //                 _context.ResourceAttendances.Add(attendanceRecord);
// //                 await _context.SaveChangesAsync();

// //                 return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
// //             }
// //             catch (Exception ex)
// //             {
// //                 // 🔥 FIX 2: NEVER FAIL SILENTLY AGAIN. Send the exact crash reason to Android!
// //                 return StatusCode(500, new { message = $"Backend Crash: {ex.Message}" });
// //             }
// //         }

// //         [HttpPut("checkout")]
// //         public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
// //         {
// //             try
// //             {
// //                 if (request == null) return BadRequest(new { message = "Invalid payload" });

// //                 if (!int.TryParse(request.CoachId, out int resourceIdInt))
// //                     return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

// //                 // Find their active session
// //                 var attendanceRecord = await _context.ResourceAttendances
// //                     .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

// //                 if (attendanceRecord == null) return NotFound(new { message = "No active check-in found to check out from." });

// //                 // 🔥 BULLETPROOF BASE64 CLEANER
// //                 string cleanBase64 = request.CheckOutImage ?? "";
// //                 if (cleanBase64.Contains(",")) {
// //                     cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
// //                 }

// //                 string? imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkout_{request.CoachId}_{DateTime.UtcNow.Ticks}");

// //                 attendanceRecord.CheckOutTime = DateTime.UtcNow.TimeOfDay;
// //                 attendanceRecord.CheckOutImage = imageUrl; 
// //                 attendanceRecord.CheckOutLocation = request.CheckOutLocation;

// //                 _context.ResourceAttendances.Update(attendanceRecord);
// //                 await _context.SaveChangesAsync();

// //                 return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
// //             }
// //             catch (Exception ex)
// //             {
// //                 // 🔥 SEND CRASH TO ANDROID
// //                 return StatusCode(500, new { message = $"Backend Crash: {ex.Message}" });
// //             }
// //         }

// //         // ========================================================
// //         // 🔥 BUSINESS CENTRAL INTEGRATION API
// //         // ========================================================
// //         [HttpGet("bc-sync/{empId}")]
// //         public async Task<IActionResult> GetAttendanceForBusinessCentral(string empId)
// //         {
// //             try
// //             {
// //                 // 1. Look up the ResourceId using the EmpId
// //                 var resource = await _context.ResourceMasters
// //                     .FirstOrDefaultAsync(r => r.EmpId == empId);

// //                 if (resource == null) 
// //                     return NotFound(new { message = $"No counselor found with EmpId: {empId}" });

// //                 // 2. Fetch all attendance records for this ResourceId
// //                 var attendances = await _context.ResourceAttendances
// //                     .Where(a => a.ResourceId == resource.Id)
// //                     .OrderByDescending(a => a.CheckInDate)
// //                     .ToListAsync();

// //                 // 3. Map and format the data strictly to Business Central requirements
// //                 var bcPayload = attendances.Select(a => new
// //                 {
// //                     Id = a.Id,
// //                     ResourceId = a.ResourceId,
// //                     EmpId = resource.EmpId,         // Included for validation
// //                     SchoolId = a.SchoolId,
// //                     SessionId = a.SessionId,
                    
// //                     // Format Date to strictly "dd-MM-yyyy"
// //                     Date = a.CheckInDate.ToString("dd-MM-yyyy"),
                    
// //                     // Format Time to Decimal String (e.g., 09:15:00 -> "9.15")
// //                     // Uses :D2 to ensure 9:05 AM becomes "9.05" and not "9.5"
// //                     CheckInTime = a.CheckInTime.HasValue 
// //                         ? $"{a.CheckInTime.Value.Hours}.{a.CheckInTime.Value.Minutes:D2}" 
// //                         : "",
                        
// //                     CheckOutTime = a.CheckOutTime.HasValue 
// //                         ? $"{a.CheckOutTime.Value.Hours}.{a.CheckOutTime.Value.Minutes:D2}" 
// //                         : "",

// //                     CheckInImage = a.CheckInImage ?? "",
// //                     CheckOutImage = a.CheckOutImage ?? "",
// //                     CheckInLocation = a.CheckInLocation ?? "",
// //                     CheckOutLocation = a.CheckOutLocation ?? "",
                    
// //                     // Standard timestamp for sync tracking
// //                     CreatedAt = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
// //                 });

// //                 return Ok(bcPayload);
// //             }
// //             catch (Exception ex)
// //             {
// //                 return StatusCode(500, new { message = $"BC Sync Error: {ex.Message}" });
// //             }
// //         }
// //     }
// // }