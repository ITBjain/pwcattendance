using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("ParentsEnrollments")]
    public class ParentsEnrollment
    {
        [Key]
        public int Id { get; set; }
        public string SchoolId { get; set; } = string.Empty;
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
        public string? ChildName { get; set; }
        public string? PaymentStatus { get; set; }
    }
}