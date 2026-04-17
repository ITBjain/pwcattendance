using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("SchoolMaster")]
    public class SchoolMaster
    {
        [Key]
        public string SchoolId { get; set; } = string.Empty; // e.g., "Sch0004"
        public string SchoolName { get; set; } = string.Empty;
        public string? SchoolCity { get; set; }
        public int CounselorId { get; set; } // Links to ResourceMaster.Id
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? SchoolAddress { get; set; }
    }
}