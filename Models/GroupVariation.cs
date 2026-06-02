using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace PwcApi.Models
{
        [Table("GroupaVariations")]
public class GroupVariation
    {
        [Key]
        public int Id { get; set; }
        public int SessionId { get; set; }
        
        // 🔥 Add '= string.Empty;' to these three strings
        public string AgeGroup { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public string Days { get; set; } = string.Empty;
        public string TimeSlot { get; set; } = string.Empty;
        public int Capacity { get; set; }

        // 🔥 Initialize the empty list
        public ICollection<ChildAttendance> ChildAttendances { get; set; } = new List<ChildAttendance>();
    }
}