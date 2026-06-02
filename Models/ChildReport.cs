using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models 
{
    public class ChildReport
    {
        [Key]
        public int Id { get; set; }
        public int ChildEnrollmentId { get; set; }
        public int GroupVariationId { get; set; }
        public DateTime ReportDate { get; set; }
        public string? TeacherNotes { get; set; }
        public string? MediaUrl { get; set; }
        public bool SentViaWhatsApp { get; set; }
        public bool SentViaEmail { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}