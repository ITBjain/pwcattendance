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
            if (request == null) return BadRequest("Invalid payload");

            if (!int.TryParse(request.CoachId, out int resourceIdInt))
                return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

            // 1. Verify the user exists
            var resourceExists = await _context.ResourceMasters.AnyAsync(r => r.Id == resourceIdInt);
            if (!resourceExists) return NotFound(new { message = $"Counselor with ID {request.CoachId} does not exist." });

            // 🔥 FIX 2: Check if they are already checked in by looking for a missing CheckOutTime
            var existingSession = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

            if (existingSession != null) return Conflict(new { message = "You are already checked in. Please check out first." });

            string? imageUrl = await _r2Service.UploadBase64ImageAsync(request.CheckInImage, $"checkin_{request.CoachId}");

            var currentTime = DateTime.UtcNow;

            // 3. Save to Database (We completely ignore SessionId now)
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

        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
        {
            if (request == null) return BadRequest("Invalid payload");

            if (!int.TryParse(request.CoachId, out int resourceIdInt))
                return BadRequest(new { message = "Invalid Coach ID format. Must be a number." });

            // 🔥 FIX 3: Find their active session (the one where CheckOutTime is null)
            var attendanceRecord = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.ResourceId == resourceIdInt && a.CheckOutTime == null);

            if (attendanceRecord == null) return NotFound(new { message = "No active check-in found to check out from." });

            string? imageUrl = await _r2Service.UploadBase64ImageAsync(request.CheckOutImage, $"checkout_{request.CoachId}");

            attendanceRecord.CheckOutTime = DateTime.UtcNow.TimeOfDay;
            attendanceRecord.CheckOutImage = imageUrl; 
            attendanceRecord.CheckOutLocation = request.CheckOutLocation;

            _context.ResourceAttendances.Update(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
        }
    }
}