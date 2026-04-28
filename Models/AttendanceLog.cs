using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("AttendanceLogs")]
    public class AttendanceLog
    {
        [Key]
        public string SessionId { get; set; } = string.Empty; 
        public string CoachId { get; set; } = string.Empty;
        
        public DateTime CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        
        public string? CheckOutLocation { get; set; }
        public string? CheckOutImage { get; set; }

        // 🔥 REAL-TIME TRACKING METRICS
        public int TotalCalls { get; set; }
        public int TotalEmails { get; set; }
        public int TotalWhatsApp { get; set; }
        public int TotalParentsTargeted { get; set; }
        
        public string? Remark { get; set; }
        public string Status { get; set; } = "Active"; // "Active" or "Completed"
    }
}