using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khrenkov.top.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.CheckConstraint("CK_Files_Name", "length(Name) > 0");
                    table.CheckConstraint("CK_Files_Size", "Size >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SchemaMigrations",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaMigrations", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "ShareLinks",
                columns: table => new
                {
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<long>(type: "INTEGER", nullable: false),
                    RemainingDownloads = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareLinks", x => x.Token);
                    table.CheckConstraint("CK_ShareLinks_RemainingDownloads", "RemainingDownloads >= 0");
                    table.CheckConstraint("CK_ShareLinks_Token", "length(Token) > 0");
                    table.ForeignKey(
                        name: "FK_ShareLinks_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_Name",
                table: "Files",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_FileId",
                table: "ShareLinks",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchemaMigrations");

            migrationBuilder.DropTable(
                name: "ShareLinks");

            migrationBuilder.DropTable(
                name: "Files");
        }
    }
}
