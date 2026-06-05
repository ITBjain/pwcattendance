      public class BulkWhatsAppRequest
    {
        public int CounselorId { get; set; }
        public List<int> ParentIds { get; set; } = new List<int>(); // 🔥 Initialize
        public string Message { get; set; } = string.Empty;         // 🔥 Initialize
        public string MediaType { get; set; } = string.Empty;       // 🔥 Initialize
        public string MediaBase64 { get; set; } = string.Empty;     // 🔥 Initialize
        public string FileName { get; set; } = string.Empty;
    }