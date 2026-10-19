namespace Khrenkov.top.Data.Entities;

public sealed class ShareLinkEntity
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public long FileId { get; set; }
    public FileEntity File { get; set; } = null!;
    public int RemainingDownloads { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
