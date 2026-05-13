public class UpdateParentStatusRequest
{
    public int ParentId { get; set; }
    public string InterestLevel { get; set; } = string.Empty;
    public string? FollowUpDate { get; set; } // Format from Android: "yyyy-MM-dd"
}