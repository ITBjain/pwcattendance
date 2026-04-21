namespace PwcApi.DTOs
{
    // Requests
    public class LoginRequest 
    { 
        public string Email { get; set; } = string.Empty; 
        public string Password { get; set; } = string.Empty; 
    }

    // Responses
    public class LoginResponse
    {
        public int CounselorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string EmpId { get; set; } = string.Empty;
    }

    public class CentreResponse
    {
        public string SchoolId { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
    }

   public class ParentResponse
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty; // "Potential" or "Enrolled"
        
        // Common Fields
        public string? ParentName { get; set; }
        public string? ParentEmail { get; set; }
        public string? ParentPhone { get; set; }
        public string? ChildName { get; set; }
        public DateTime? ChildDOB { get; set; }
        public string? ChildSchoolName { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? MediaConsent { get; set; }

        // Potential Specific Fields
        public string? Remark { get; set; }

        // Enrollment Specific Fields
        public string? PaymentStatus { get; set; }
        public DateTime? PaymentDate { get; set; }
        public decimal? PaymentAmount { get; set; }
        public string? BillingAddress { get; set; }
        public string? BillingCity { get; set; }
        public string? BillingState { get; set; }
        public string? BillingPincode { get; set; }
        public string? ChildSchoolCity { get; set; }
        
        // Session Details (Enrollment only)
        public int? SessionId { get; set; }
        public string? SessionName { get; set; }
        public string? SessionAgeGroup { get; set; }
        public string? SessionDays { get; set; }
        public string? SessionFrequency { get; set; }
        public string? SessionTimeSlot { get; set; }
        
        // Discount
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
    }
}