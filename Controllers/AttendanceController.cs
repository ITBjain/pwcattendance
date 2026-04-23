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
    }
}