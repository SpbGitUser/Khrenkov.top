namespace Khrenkov.top.Data.Entities;

// Retains the old import ledger so adopting an existing database cannot revive legacy links.
public sealed class DataMigration
{
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
