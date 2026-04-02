using System;
using System.ComponentModel.DataAnnotations;

namespace PwcApi.Models
{
    public class CoachAttendance
    {
        [Key]
        public int Id { get; set; }
        public string CoachId { get; set; } = string.Empty;
        public string SchoolId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public string? CheckInImage { get; set; }
        public string? CheckOutImage { get; set; }

        public string? CheckInLocation { get; set; }
        public string? CheckOutLocation { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}