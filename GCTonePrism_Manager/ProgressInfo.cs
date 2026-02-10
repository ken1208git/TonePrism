namespace GCTonePrism.Manager
{
    public class ProgressInfo
    {
        public int Percentage { get; set; }
        public string Message { get; set; }
        public string Detail { get; set; }

        public ProgressInfo(int percentage, string message, string detail = "")
        {
            Percentage = percentage;
            Message = message;
            Detail = detail;
        }
    }
}
