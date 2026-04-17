using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("ResourceMasters")]
    public class ResourceMaster
    {
        [Key]
        public int Id { get; set; } // This is your CounselorId
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int IsActive { get; set; }
        public string CompanyEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EmpId { get; set; } = string.Empty;
    }
}