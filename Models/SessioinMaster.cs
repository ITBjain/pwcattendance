using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    // This tells Entity Framework the exact name of the table in MySQL
    [Table("SessionMasters")] 
    public class SessionMaster
    {
        [Key]
        public int Id { get; set; }
        
        public string? SessionName { get; set; }
        public int IsActive { get; set; } 
        public int? Capacity { get; set; }
        public DateTime? EndDate { get; set; }
        public int? BaseFrequency { get; set; }
        public DateTime? StartDate { get; set; }
        public string? CoachId { get; set; }
        public string? CoachName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? SchoolId { get; set; }
    }
}