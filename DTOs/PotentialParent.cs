using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("Potential_Parents")]
    public class PotentialParent
    {
        [Key]
        public int Id { get; set; }
        public string? Parent_Name { get; set; }
        public string? Parent_Phone { get; set; }
        public string? Parent_Email { get; set; }
        public string? Child_Name { get; set; }
        public DateTime? Child_DOB { get; set; }
        public string? ChildSchoolName { get; set; }
        
        // In your DB, 'School_Name' actually holds the SchoolId (e.g., "Sch0001")
        public string? School_Name { get; set; } 
        public string? School_Phone { get; set; }
        public string? School_Email { get; set; }
        public string? School_City { get; set; }
        public string? School_Location { get; set; }
        
        public string? Remark { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? MediaConsent { get; set; }
    }
}