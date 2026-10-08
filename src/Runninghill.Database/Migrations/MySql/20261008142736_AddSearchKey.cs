using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.MySql
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
                type: "varchar(160)",
                maxLength: 160,
                nullable: true);
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
