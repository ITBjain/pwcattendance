using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using PwcApi.Services;
using System;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service;

        public AttendanceController(AppDbContext context, R2StorageService r2Service)
        {
            _context = context;
            _r2Service = r2Service;
        }

       [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
        {
            try 
            {
                if (request == null) return BadRequest(new { message = "Invalid payload" });

                if (!int.TryParse(request.CoachId, out int resourceIdInt))
                    return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

                // 1. Verify the user exists
                var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceIdInt);
                if (!resourceExists) return NotFound(new { message = $"Counselor with ID {request.CoachId} does not exist." });

                // 2. Check if they are already checked in
                var existingSession = await _context.ResourceAttendances
                    .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

                if (existingSession != null) return Conflict(new { message = "You are already checked in. Please check out first." });

                // 🔥 FIX 1: BULLETPROOF BASE64 CLEANER
                // This ensures _r2Service never crashes regardless of what Android sends
                string cleanBase64 = request.CheckInImage ?? "";
                if (cleanBase64.Contains(",")) {
                    cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
                }

                // Upload Image (Added a timestamp to prevent duplicate filename overwrites)
                string? imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkin_{request.CoachId}_{DateTime.UtcNow.Ticks}");

                var currentTime = DateTime.UtcNow;

                // 3. Save to Database
                var attendanceRecord = new ResourceAttendance
                {
                    ResourceId = resourceIdInt,   
                    SchoolId = request.SchoolId,  
                    CheckInDate = currentTime.Date,
                    CheckInTime = currentTime.TimeOfDay, 
                    CheckInImage = imageUrl, 
                    CheckInLocation = request.CheckInLocation
                };

                _context.ResourceAttendances.Add(attendanceRecord);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
            }
            catch (Exception ex)
            {
                // 🔥 FIX 2: NEVER FAIL SILENTLY AGAIN. Send the exact crash reason to Android!
                return StatusCode(500, new { message = $"Backend Crash: {ex.Message}" });
            }
        }

        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
        {
            try
            {
                if (request == null) return BadRequest(new { message = "Invalid payload" });

                if (!int.TryParse(request.CoachId, out int resourceIdInt))
                    return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

                // Find their active session
                var attendanceRecord = await _context.ResourceAttendances
                    .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

                if (attendanceRecord == null) return NotFound(new { message = "No active check-in found to check out from." });

                // 🔥 BULLETPROOF BASE64 CLEANER
                string cleanBase64 = request.CheckOutImage ?? "";
                if (cleanBase64.Contains(",")) {
                    cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
                }

                string? imageUrl = await _r2Service.UploadBase64ImageAsync(cleanBase64, $"checkout_{request.CoachId}_{DateTime.UtcNow.Ticks}");

                attendanceRecord.CheckOutTime = DateTime.UtcNow.TimeOfDay;
                attendanceRecord.CheckOutImage = imageUrl; 
                attendanceRecord.CheckOutLocation = request.CheckOutLocation;

                _context.ResourceAttendances.Update(attendanceRecord);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
            }
            catch (Exception ex)
            {
                // 🔥 SEND CRASH TO ANDROID
                return StatusCode(500, new { message = $"Backend Crash: {ex.Message}" });
            }
        }

        // ========================================================
        // 🔥 BUSINESS CENTRAL INTEGRATION API
        // ========================================================
        [HttpGet("bc-sync/{empId}")]
        public async Task<IActionResult> GetAttendanceForBusinessCentral(string empId)
        {
            try
            {
                // 1. Look up the ResourceId using the EmpId
                var resource = await _context.ResourceMasters
                    .FirstOrDefaultAsync(r => r.EmpId == empId);

                if (resource == null) 
                    return NotFound(new { message = $"No counselor found with EmpId: {empId}" });

                // 2. Fetch all attendance records for this ResourceId
                var attendances = await _context.ResourceAttendances
                    .Where(a => a.ResourceId == resource.Id)
                    .OrderByDescending(a => a.CheckInDate)
                    .ToListAsync();

                // 3. Map and format the data strictly to Business Central requirements
                var bcPayload = attendances.Select(a => new
                {
                    Id = a.Id,
                    ResourceId = a.ResourceId,
                    EmpId = resource.EmpId,         // Included for validation
                    SchoolId = a.SchoolId,
                    SessionId = a.SessionId,
                    
                    // Format Date to strictly "dd-MM-yyyy"
                    Date = a.CheckInDate.ToString("dd-MM-yyyy"),
                    
                    // Format Time to Decimal String (e.g., 09:15:00 -> "9.15")
                    // Uses :D2 to ensure 9:05 AM becomes "9.05" and not "9.5"
                    CheckInTime = a.CheckInTime.HasValue 
                        ? $"{a.CheckInTime.Value.Hours}.{a.CheckInTime.Value.Minutes:D2}" 
                        : "",
                        
                    CheckOutTime = a.CheckOutTime.HasValue 
                        ? $"{a.CheckOutTime.Value.Hours}.{a.CheckOutTime.Value.Minutes:D2}" 
                        : "",

                    CheckInImage = a.CheckInImage ?? "",
                    CheckOutImage = a.CheckOutImage ?? "",
                    CheckInLocation = a.CheckInLocation ?? "",
                    CheckOutLocation = a.CheckOutLocation ?? "",
                    
                    // Standard timestamp for sync tracking
                    CreatedAt = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                });

                return Ok(bcPayload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"BC Sync Error: {ex.Message}" });
            }
        }
    }
}