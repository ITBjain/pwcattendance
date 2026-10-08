namespace PwcApi.DTOs
{
    /// <summary>
    /// The coach's currently OPEN attendance session (punched in, not yet punched out).
    /// Returned by GET api/Attendance/status/{coachId}, by check-in, and inside 409 "already checked in" errors.
    /// </summary>
    public class AttendanceSessionDto
    {
        public int RecordId { get; set; }
        public string? Type { get; set; }
        public string? SchoolId { get; set; }
        public string? SchoolName { get; set; }

        public string CheckInDate { get; set; } = string.Empty;   // yyyy-MM-dd (IST)
        public string CheckInTime { get; set; } = string.Empty;   // HH:mm:ss  (IST)
        public string CheckInAt { get; set; } = string.Empty;     // 2026-10-07T09:12:30+05:30

        /// <summary>Seconds since punch-in, calculated on the server (immune to wrong phone clocks).</summary>
        public long ElapsedSeconds { get; set; }

        /// <summary>True when the coach punched in on an earlier day and never punched out.</summary>
        public bool IsPreviousDay { get; set; }

        public int TotalCalls { get; set; }
        public int TotalEmails { get; set; }
        public int TotalWhatsApp { get; set; }
        public int TotalParentsTargeted { get; set; }
    }
}
