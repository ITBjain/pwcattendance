using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using System;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AttendanceController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/attendance/checkin
        [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
        {
            if (request == null) return BadRequest("Invalid payload");

            // Check if already checked in for this specific session
            var existingSession = await _context.CoachAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.CoachId == request.CoachId);

            if (existingSession != null)
            {
                return Conflict(new { message = "Coach is already checked into this session." });
            }

            var currentTime = DateTime.UtcNow;

            var attendanceRecord = new CoachAttendance
            {
                CoachId = request.CoachId,
                SchoolId = request.SchoolId,
                SessionId = request.SessionId,
                Date = currentTime.Date,
                CheckInTime = currentTime,
                CheckInImage = request.CheckInImage, // Assuming you upload the image first and pass the URL here
                CheckInLocation = request.CheckInLocation
            };

            _context.CoachAttendances.Add(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
        }

        // PUT: api/attendance/checkout
        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
        {
            if (request == null) return BadRequest("Invalid payload");

            // Find the active check-in record for this session
            var attendanceRecord = await _context.CoachAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.CoachId == request.CoachId);

            if (attendanceRecord == null)
            {
                return NotFound(new { message = "Active check-in session not found." });
            }

            if (attendanceRecord.CheckOutTime != null)
            {
                return BadRequest(new { message = "Coach has already checked out of this session." });
            }

            attendanceRecord.CheckOutTime = DateTime.UtcNow;
            attendanceRecord.CheckOutImage = request.CheckOutImage;
            attendanceRecord.CheckOutLocation = request.CheckOutLocation;

            _context.CoachAttendances.Update(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
        }
    }
}