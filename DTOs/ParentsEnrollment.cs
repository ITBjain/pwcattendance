using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Amazon.S3.Model;

namespace PwcApi.Models
{
    [Table("ParentEnrollments")]
    public class ParentsEnrollment
    {
        [Key]
        public int Id { get; set; }
        public string? SchoolId { get; set; }
        public string? ParentName { get; set; }
        public string? ParentEmail { get; set; }
        public string? ParentPhone { get; set; }
        public string? ChildName { get; set; }
        public string? ChildSchoolName { get; set; }
        public string? ChildSchoolCity { get; set; }
        public DateTime? ChildDOB { get; set; }
        
        // Payment & Billing
        public string? PaymentStatus { get; set; }
        public DateTime? PaymentDate { get; set; }
        public decimal? PaymentAmount { get; set; }
        public string? BillingAddress { get; set; }
        public string? BillingCity { get; set; }
        public string? BillingPincode { get; set; }
        public string? BillingState { get; set; }
        
        // Session Info
        public int? SessionId { get; set; }
        public string? SessionName { get; set; }
        public string? SessionAgeGroup { get; set; }
        public string? SessionDays { get; set; }
        public string? SessionFrequency { get; set; }
        public string? SessionTimeSlot { get; set; }
        
        // Discounts & Meta
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? MediaConsent { get; set; }
    }
}