namespace DoggyDrop.Services
{
    public class EmailSettings
    {
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; } = string.Empty;
        public string SmtpPass { get; set; } = string.Empty;
        public string SenderName { get; set; } = "DoggyDrop";
        public string SenderEmail { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
    }
}
