      public class BulkWhatsAppRequest
    {
        public int CounselorId { get; set; }
        public List<int> ParentIds { get; set; }
        public string Message { get; set; }
        
        // Fields for sending Media (Images/Documents)
        public string MediaType { get; set; } 
        public string MediaBase64 { get; set; } 
        public string FileName { get; set; } 
    }