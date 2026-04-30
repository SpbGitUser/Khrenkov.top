namespace Khrenkov.top.Models
{
    public class ShareLink
    {
        public string Token { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public int RemainingDownloads { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
