using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PwcApi.Models
{
    /// <summary>
    /// One "message parents of this batch" job created by a coach (text + optional activity photos).
    /// Sent in the background by WhatsAppBroadcastWorker so the app never waits/times out.
    /// </summary>
    [Table("BatchBroadcasts")]
    public class BatchBroadcast
    {
        [Key]
        public int Id { get; set; }

        public int BatchId { get; set; }          // GroupVariations.Id
        public int CoachId { get; set; }          // ResourceMasters.Id

        public string? Message { get; set; }

        /// <summary>JSON array of permanent R2 image URLs (kept for history).</summary>
        public string? MediaUrls { get; set; }

        /// <summary>JSON array of base64 data URIs used for sending. Cleared once the job finishes.</summary>
        public string? MediaPayloads { get; set; }

        /// <summary>Queued | Sending | Completed | CompletedWithErrors | Failed</summary>
        public string Status { get; set; } = "Queued";

        public int TotalRecipients { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; }   // IST
        public DateTime? StartedAt { get; set; }  // IST
        public DateTime? CompletedAt { get; set; } // IST
    }

    [Table("BatchBroadcastRecipients")]
    public class BatchBroadcastRecipient
    {
        [Key]
        public int Id { get; set; }

        public int BroadcastId { get; set; }
        public int EnrollmentId { get; set; }     // ParentEnrollments.Id (first child for this phone)

        public string? ParentName { get; set; }
        public string? ChildName { get; set; }    // "Aarav" or "Aarav & Kiara" for siblings
        public string Phone { get; set; } = string.Empty; // normalised, e.g. 919876543210

        /// <summary>Pending | Sent | Failed</summary>
        public string Status { get; set; } = "Pending";
        public string? Error { get; set; }
        public DateTime? SentAt { get; set; }     // IST
    }
}
