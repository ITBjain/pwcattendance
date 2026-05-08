using System.Collections.Generic;

namespace PwcApi.DTOs
{
    // Your existing CheckOutRequest (make sure it has the new tracking fields!)
    public class CheckOutRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string CoachId { get; set; } = string.Empty;
        public string CheckOutImage { get; set; } = string.Empty;
        public string CheckOutLocation { get; set; } = string.Empty;
        
        // Automated Tracking Fields
        public int TotalCalls { get; set; }
        public int TotalEmails { get; set; }
        public int TotalWhatsApp { get; set; }
        public int TotalParentsTargeted { get; set; }
        public string? Remark { get; set; } 
           public string Type { get; set; } = string.Empty;
        public string? AttendanceRemark { get; set; }
    }

    // 🔥 ADD THIS MISSING CLASS HERE!
    public class SyncActivityRequest
    {
        public string CoachId { get; set; } = string.Empty;
        public int TotalCalls { get; set; }
        public int TotalEmails { get; set; }
        public int TotalWhatsApp { get; set; }
        public int TotalParentsTargeted { get; set; }
     
    }
    
    // 🔥 AND ADD THIS FOR THE BULK EMAIL!
    public class BulkMessageRequest
    {
        public List<string> Emails { get; set; } = new List<string>();
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
    public class LogInteractionRequest
    {
        public int ResourceId { get; set; }
        public int ParentId { get; set; }
        public string InteractionType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
    }
}