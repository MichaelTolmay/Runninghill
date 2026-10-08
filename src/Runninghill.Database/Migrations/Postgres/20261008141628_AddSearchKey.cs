using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_word",
                schema: "runninghill",
                table: "words",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                collation: "C");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "normalized_word",
                schema: "runninghill",
                table: "words");
        }
    }
}
