using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using PwcApi.Services; // Add this
using System;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service; // Inject Service

        public AttendanceController(AppDbContext context, R2StorageService r2Service)
        {
            _context = context;
            _r2Service = r2Service;
        }

        [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
        {
            if (request == null) return BadRequest("Invalid payload");

            var existingSession = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.ResourceId == request.CoachId);

            if (existingSession != null) return Conflict(new { message = "Already checked in." });

            // 🔥 UPLOAD IMAGE TO R2 🔥
            string imageUrl = await _r2Service.UploadBase64ImageAsync(request.CheckInImage, $"checkin_{request.CoachId}");

            var currentTime = DateTime.UtcNow;

            var attendanceRecord = new ResourceAttendance
            {
                ResourceId = request.CoachId,
                SchoolId = request.SchoolId,
                SessionId = request.SessionId,
                CheckInDate = currentTime.Date,
                CheckInTime = currentTime,
                CheckInImage = imageUrl, // Save the sleek R2 URL, not the massive Base64 string!
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

            var attendanceRecord = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.ResourceId == request.CoachId);

            if (attendanceRecord == null) return NotFound(new { message = "Session not found." });
            if (attendanceRecord.CheckOutTime != null) return BadRequest(new { message = "Already checked out." });

            // 🔥 UPLOAD IMAGE TO R2 🔥
            string imageUrl = await _r2Service.UploadBase64ImageAsync(request.CheckOutImage, $"checkout_{request.CoachId}");

            attendanceRecord.CheckOutTime = DateTime.UtcNow;
            attendanceRecord.CheckOutImage = imageUrl; // Save the URL
            attendanceRecord.CheckOutLocation = request.CheckOutLocation;

            _context.ResourceAttendances.Update(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
        }
    }
}