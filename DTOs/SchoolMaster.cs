using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    [Table("SchoolMaster")]
    public class SchoolMaster
    {
        [Key]
        public string SchoolId { get; set; } = string.Empty; // e.g., "Sch0004"
        public string? SchoolName { get; set; }
        public string? SchoolEmail { get; set; }
        public string? SchoolPhone { get; set; }
        public int? OnBoardingId { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? BillingAddress { get; set; }
        public string? ContactPersonEmail { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonPhone { get; set; }
        public string? OwnerEmail { get; set; }
        public string? OwnerName { get; set; }
        public string? OwnerPhone { get; set; }
        public string? PrincipalEmail { get; set; }
        public string? PrincipalName { get; set; }
        public string? PrincipalPhone { get; set; }
        public string? SchoolAddress { get; set; }
        public string? SchoolCity { get; set; }
        public string? SchoolCountry { get; set; }
        public string? SchoolLocation { get; set; }
        public string? SchoolPincode { get; set; }
        public string? SchoolState { get; set; }
        public string? ShippingAddress { get; set; }
        public string? Plan { get; set; }
        public int? IsEnrolled { get; set; }
        public int? CounselorId { get; set; }
        public decimal? FacilitationCharges { get; set; }
        public int? SalesPersonId { get; set; }
        
        // Session Contacts
        public string? SessionContact1Email { get; set; }
        public string? SessionContact1Name { get; set; }
        public string? SessionContact1Phone { get; set; }
        public string? SessionContact1Position { get; set; }
        public string? SessionContact2Email { get; set; }
        public string? SessionContact2Name { get; set; }
        public string? SessionContact2Phone { get; set; }
        public string? SessionContact2Position { get; set; }
        public string? SessionContact3Email { get; set; }
        public string? SessionContact3Name { get; set; }
        public string? SessionContact3Phone { get; set; }
        public string? SessionContact3Position { get; set; }
        public string? SessionContact4Email { get; set; }
        public string? SessionContact4Name { get; set; }
        public string? SessionContact4Phone { get; set; }
        public string? SessionContact4Position { get; set; }
        
        public string? ActivityCount { get; set; }
        public string? PromoCode { get; set; }
        public decimal? PromoDiscount { get; set; }
    }
}