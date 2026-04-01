namespace PwcApi.DTOs
{
    public class CheckInRequest
    {
        public string CoachId { get; set; } = string.Empty;
        public string SchoolId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string CheckInImage { get; set; } = string.Empty; 
        public string CheckInLocation { get; set; } = string.Empty;
    }
}