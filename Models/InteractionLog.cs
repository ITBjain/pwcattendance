using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
        [Table("InteractionLogs")]
    public class InteractionLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int ResourceId { get; set; } 
        public int ParentId { get; set; }   
        public string InteractionType { get; set; } = string.Empty; // "Call", "WhatsApp", "Email"
        public string Status { get; set; } = string.Empty; // "Connected", "Unanswered", "Sent"
        public int DurationSeconds { get; set; } = 0; 
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}