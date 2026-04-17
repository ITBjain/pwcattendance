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
        public string? Child_Name { get; set; }
        public string School_Name { get; set; } = string.Empty; // Note: Holds SchoolId from DB
    }
}