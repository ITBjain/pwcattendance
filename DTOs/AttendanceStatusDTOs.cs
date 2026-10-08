
namespace PwcApi.DTOs
{
    /// <summary>
    /// The coach's currently OPEN attendance session (punched in, not yet punched out).
    /// Returned by GET api/Attendance/status/{coachId}, by check-in, and inside 409 "already checked in" errors.
    /// </summary>
    public class AttendanceSessionDto
    {
        public int RecordId { get; set; }
        public string? Type { get; set; }
        public string? SchoolId { get; set; }
        public string? SchoolName { get; set; }

        public string CheckInDate { get; set; } = string.Empty;   // yyyy-MM-dd (IST)
        public string CheckInTime { get; set; } = string.Empty;   // HH:mm:ss  (IST)
        public string CheckInAt { get; set; } = string.Empty;     // 2026-10-07T09:12:30+05:30

        /// <summary>Seconds since punch-in, calculated on the server (immune to wrong phone clocks).</summary>
        public long ElapsedSeconds { get; set; }

        /// <summary>True when the coach punched in on an earlier day and never punched out.</summary>
        public bool IsPreviousDay { get; set; }

        public int TotalCalls { get; set; }
        public int TotalEmails { get; set; }
        public int TotalWhatsApp { get; set; }
        public int TotalParentsTargeted { get; set; }
    }
}



// namespace PwcApi.DTOs
// {
//     // Requests
//     public class LoginRequest 
//     { 
//         public string Email { get; set; } = string.Empty; 
//         public string Password { get; set; } = string.Empty; 
//     }

//     // Responses
//     public class LoginResponse
//     {
//         public int CounselorId { get; set; }
//         public string Name { get; set; } = string.Empty;
//         public string EmpId { get; set; } = string.Empty;
//         public string Role { get; set; } = string.Empty; // 🔥 NEW: "Counselor" / "Coach" so the app can show role-based features
//     }

//     public class CentreResponse
//     {
//         public string SchoolId { get; set; } = string.Empty;
//         public string SchoolName { get; set; } = string.Empty;
//         public string Address { get; set; } = string.Empty;
//         public string ContactName { get; set; } = string.Empty;
//         public string ContactPhone { get; set; } = string.Empty;
        
//     }

//    public class ParentResponse
//     {
//         public int Id { get; set; }
//         public string Status { get; set; } = string.Empty; // "Potential" or "Enrolled"
        
//         // Common Fields
//         public string? ParentName { get; set; }
//         public string? ParentEmail { get; set; }
//         public string? ParentPhone { get; set; }
//         public string? ChildName { get; set; }
//         public DateTime? ChildDOB { get; set; }
//         public string? ChildSchoolName { get; set; }
//         public DateTime? CreatedAt { get; set; }
//         public int? MediaConsent { get; set; }

//         // Potential Specific Fields
//         public string? Remark { get; set; }

//         // Enrollment Specific Fields
//         public string? PaymentStatus { get; set; }
//         public DateTime? PaymentDate { get; set; }
//         public decimal? PaymentAmount { get; set; }
//         public string? BillingAddress { get; set; }
//         public string? BillingCity { get; set; }
//         public string? BillingState { get; set; }
//         public string? BillingPincode { get; set; }
//         public string? ChildSchoolCity { get; set; }
        
//         // Session Details (Enrollment only)
//         public int? SessionId { get; set; }
//         public string? SessionName { get; set; }
//         public string? SessionAgeGroup { get; set; }
//         public string? SessionDays { get; set; }
//         public string? SessionFrequency { get; set; }
//         public string? SessionTimeSlot { get; set; }
        
//         // Discount
//         public decimal? DiscountAmount { get; set; }
//         public string? DiscountCode { get; set; }
//         // 🔥 NEW FIELDS TO SEND BACK TO ANDROID:
//     public string? InterestLevel { get; set; }
//     public string? FollowUpDate { get; set; }
//     public bool HasBeenContacted { get; set; }
//     }
// }



// // namespace PwcApi.DTOs
// // {
// //     /// <summary>
// //     /// The coach's currently OPEN attendance session (punched in, not yet punched out).
// //     /// Returned by GET api/Attendance/status/{coachId}, by check-in, and inside 409 "already checked in" errors.
// //     /// </summary>
// //     public class AttendanceSessionDto
// //     {
// //         public int RecordId { get; set; }
// //         public string? Type { get; set; }
// //         public string? SchoolId { get; set; }
// //         public string? SchoolName { get; set; }

// //         public string CheckInDate { get; set; } = string.Empty;   // yyyy-MM-dd (IST)
// //         public string CheckInTime { get; set; } = string.Empty;   // HH:mm:ss  (IST)
// //         public string CheckInAt { get; set; } = string.Empty;     // 2026-10-07T09:12:30+05:30

// //         /// <summary>Seconds since punch-in, calculated on the server (immune to wrong phone clocks).</summary>
// //         public long ElapsedSeconds { get; set; }

// //         /// <summary>True when the coach punched in on an earlier day and never punched out.</summary>
// //         public bool IsPreviousDay { get; set; }

// //         public int TotalCalls { get; set; }
// //         public int TotalEmails { get; set; }
// //         public int TotalWhatsApp { get; set; }
// //         public int TotalParentsTargeted { get; set; }
// //     }
// // }
