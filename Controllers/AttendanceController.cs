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

            // Check if already checked in for this specific session using ResourceId
            var existingSession = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.ResourceId == request.CoachId);

            if (existingSession != null)
            {
                return Conflict(new { message = "Resource is already checked into this session." });
            }

            var currentTime = DateTime.UtcNow;

            var attendanceRecord = new ResourceAttendance
            {
                ResourceId = request.CoachId, // Maps the Android app's CoachId to the DB's ResourceId
                SchoolId = request.SchoolId,
                SessionId = request.SessionId,
                CheckInDate = currentTime.Date, // Replaced 'Date'
                CheckInTime = currentTime,
                CheckInImage = request.CheckInImage, 
                CheckInLocation = request.CheckInLocation
            };

            _context.ResourceAttendances.Add(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-in successful", recordId = attendanceRecord.Id });
        }

        // PUT: api/attendance/checkout
        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequest request)
        {
            if (request == null) return BadRequest("Invalid payload");

            // Find the active check-in record for this session
            var attendanceRecord = await _context.ResourceAttendances
                .FirstOrDefaultAsync(a => a.SessionId == request.SessionId && a.ResourceId == request.CoachId);

            if (attendanceRecord == null)
            {
                return NotFound(new { message = "Active check-in session not found." });
            }

            if (attendanceRecord.CheckOutTime != null)
            {
                return BadRequest(new { message = "Resource has already checked out of this session." });
            }

            attendanceRecord.CheckOutTime = DateTime.UtcNow;
            attendanceRecord.CheckOutImage = request.CheckOutImage;
            attendanceRecord.CheckOutLocation = request.CheckOutLocation;

            _context.ResourceAttendances.Update(attendanceRecord);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Check-out successful", recordId = attendanceRecord.Id });
        }
    }
}