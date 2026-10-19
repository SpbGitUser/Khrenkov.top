namespace Khrenkov.top.Data.Entities;

public sealed class FileEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public ICollection<ShareLinkEntity> ShareLinks { get; set; } = new List<ShareLinkEntity>();
}
