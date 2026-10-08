using System.Collections.Generic;

namespace PwcApi.DTOs
{
    /// <summary>POST api/BatchMessaging/broadcast</summary>
    public class BatchBroadcastRequest
    {
        public string CoachId { get; set; } = string.Empty;   // ResourceMasters.Id as string (same as check-in)
        public int BatchId { get; set; }                       // GroupVariations.Id

        /// <summary>Message text. Supports {parent} and {child} placeholders.</summary>
        public string? Message { get; set; }

        /// <summary>Activity photos as base64 JPEG/PNG (with or without "data:image/jpeg;base64," prefix). Max 6.</summary>
        public List<string> Images { get; set; } = new List<string>();

        /// <summary>Which children's parents to message. Empty = every paid enrollment in the batch.</summary>
        public List<int> EnrollmentIds { get; set; } = new List<int>();
    }
}
