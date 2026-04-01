namespace PwcApi.DTOs
{

    public class CheckOutRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string CoachId { get; set; } = string.Empty;
        public string CheckOutImage { get; set; } = string.Empty;
        public string CheckOutLocation { get; set; } = string.Empty;
    }
}