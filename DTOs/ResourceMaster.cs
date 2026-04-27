using System; // Make sure you have this at the top
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("ResourceMasters")]
    public class ResourceMaster
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int IsActive { get; set; }
        public string CompanyEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EmpId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        
        // 🔥 CHANGE THIS LINE: It must be a DateTime? because the DB uses a Date format
        public DateTime? DateOfJoining { get; set; } 
    }
}