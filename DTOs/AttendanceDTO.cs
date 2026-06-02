namespace PwcApi.DTOs // Make sure this matches your project's namespace
{
    public class MarkChildAttendanceRequest
    {
        public int EnrollmentId { get; set; }
        public int BatchId { get; set; }
        public string Action { get; set; } = string.Empty; // "IN", "OUT", or "ABSENT"
    }

    public class SendReportRequest
    {
        public int EnrollmentId { get; set; }
        public int BatchId { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string? ImageBase64 { get; set; }
        public string ParentPhone { get; set; } = string.Empty;
        public string ParentEmail { get; set; } = string.Empty;
        public bool SendWhatsApp { get; set; }
        public bool SendEmail { get; set; }
    }
}