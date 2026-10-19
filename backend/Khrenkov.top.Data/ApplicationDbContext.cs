using System.Globalization;
using Khrenkov.top.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Khrenkov.top.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<ShareLinkEntity> ShareLinks => Set<ShareLinkEntity>();
    public DbSet<DataMigration> DataMigrations => Set<DataMigration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var utc = new ValueConverter<DateTime, string>(
            value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            value => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        modelBuilder.Entity<FileEntity>(entity =>
        {
            entity.ToTable("Files", table =>
            {
                table.HasCheckConstraint("CK_Files_Name", "length(Name) > 0");
                table.HasCheckConstraint("CK_Files_Size", "Size >= 0");
            });
            entity.HasKey(file => file.Id);
            entity.Property(file => file.Name).IsRequired();
            entity.HasIndex(file => file.Name).IsUnique();
            entity.Property(file => file.ModifiedUtc).HasConversion(utc);
        });
        modelBuilder.Entity<ShareLinkEntity>(entity =>
        {
            entity.ToTable("ShareLinks", table =>
            {
                table.HasCheckConstraint("CK_ShareLinks_Token", "length(Token) > 0");
                table.HasCheckConstraint("CK_ShareLinks_RemainingDownloads", "RemainingDownloads >= 0");
            });
            entity.HasKey(link => link.Token);
            entity.HasOne(link => link.File).WithMany(file => file.ShareLinks).HasForeignKey(link => link.FileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(link => link.CreatedAtUtc).HasConversion(utc);
        });
        modelBuilder.Entity<DataMigration>(entity =>
        {
            entity.ToTable("SchemaMigrations");
            entity.HasKey(migration => migration.Version);
            entity.Property(migration => migration.Version).ValueGeneratedNever();
            entity.Property(migration => migration.Name).IsRequired();
            entity.Property(migration => migration.AppliedAtUtc).HasConversion(utc);
        });
    }
}
