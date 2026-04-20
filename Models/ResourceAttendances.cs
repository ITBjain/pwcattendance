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
        
        public int ResourceId { get; set; } 
        
        // 🔥 FIX 1: Add a '?' to make SessionId optional (nullable)
        public int? SessionId { get; set; } 
        
        public string SchoolId { get; set; } = string.Empty;
        
        public DateTime CheckInDate { get; set; } 
        public TimeSpan? CheckInTime { get; set; }
        public TimeSpan? CheckOutTime { get; set; }
        
        public string? CheckInImage { get; set; }
        public string? CheckOutImage { get; set; }
        public string? CheckInLocation { get; set; }
        public string? CheckOutLocation { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}