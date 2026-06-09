// using System;
// using System.Collections.Generic;
// using System.ComponentModel.DataAnnotations;
// using System.ComponentModel.DataAnnotations.Schema;

// namespace PegasusWellBeingClub.Models
// {
//     [Table("KitMaster")] // 🔥 Forces EF to look for the exact MySQL table name
//     public class KitMaster
//     {
//         [Key]
//         public int KitId { get; set; }

//         [Required]
//         public string Name { get; set; }
        
//         public string AgeGroup { get; set; }

//         [DataType(DataType.Date)]
//         public DateTime? StartDate { get; set; }

//         [DataType(DataType.Date)]
//         public DateTime? ExpiryDate { get; set; }

//         public string Status { get; set; } = "Live";

//         // Navigation property to fetch the PDFs
//         public virtual ICollection<KitItemMapping> KitItems { get; set; } = new List<KitItemMapping>();
//     }
// }

// // 🔥 FIX 2: Add the missing KitItemMapping class right here below KitMaster!
//     [Table("KitItemMapping")] // Ensure this matches your actual DB table name for the mapping
//     public class KitItemMapping
//     {
//         [Key]
//         public int Id { get; set; }

//         public int KitId { get; set; }
//         [ForeignKey("KitId")]
//         public virtual KitMaster Kit { get; set; }

//         public int ItemId { get; set; }
//         [ForeignKey("ItemId")]
//         public virtual ItemMaster Item { get; set; }
//     }

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("KitMasters")]
    public class KitMaster
    {
        [Key]
        public int KitId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // Added default to fix CS8618
    
        // Made nullable if it's optional, or use = string.Empty if required
        public string? AgeGroup { get; set; }

        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ExpiryDate { get; set; }

        public string Status { get; set; } = "Live";

        // Navigation property to fetch the PDFs
        public virtual ICollection<KitItemMapping> KitItems { get; set; } = new List<KitItemMapping>();
    }

    // MOVED INSIDE THE NAMESPACE
    [Table("KitItemMappings")]
    public class KitItemMapping
    {
        [Key]
        public int Id { get; set; }

        public int KitId { get; set; }
        [ForeignKey("KitId")]
        public virtual KitMaster? Kit { get; set; } // Made nullable to satisfy compiler

        public int ItemId { get; set; }
        [ForeignKey("ItemId")]
        public virtual ItemMaster? Item { get; set; } // Made nullable to satisfy compiler
    }

    // THE MISSING MODEL
    [Table("ItemMasters")]
    public class ItemMaster
    {
        [Key]
        public int ItemId { get; set; }

        [Required]
        public string ItemName { get; set; } = string.Empty;

        public string? ItemNo { get; set; }
       
        public int Sequence { get; set; }
       
        public string? LessonPlanPdf { get; set; }
       
        public string? ItemDescription { get; set; }
       
        public string? ParentEngagementRemark { get; set; }
       
        public string? LearningRemark { get; set; }
    }
}