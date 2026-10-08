using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_word",
                table: "words",
                type: "TEXT",
                maxLength: 160,
                nullable: true,
                collation: "BINARY");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "normalized_word",
                table: "words");
        }
    }
}
