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
        public string ParentName { get; set; } = string.Empty;
        public string ParentPhone { get; set; } = string.Empty;
        public string ChildName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Potential" or "Enrolled"
        public string PaymentStatus { get; set; } = string.Empty;
    }
}