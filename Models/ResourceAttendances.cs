using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("ResourceAttendances")]
    public class ResourceAttendance
    {
        [Key]
        public int Id { get; set; }
        
        // Maps to CounselorId / CoachId
        public string ResourceId { get; set; } = string.Empty; 
        
        public string SchoolId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        
        // Updated from 'Date' to 'CheckInDate'
        public DateTime CheckInDate { get; set; } 
        
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public string? CheckInImage { get; set; }
        public string? CheckOutImage { get; set; }
        public string? CheckInLocation { get; set; }
        public string? CheckOutLocation { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}