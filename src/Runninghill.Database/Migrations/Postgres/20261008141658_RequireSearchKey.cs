using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RequireSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "words_word_type",
                schema: "runninghill",
                table: "words");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_word",
                schema: "runninghill",
                table: "words",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.UpdateData(
                schema: "runninghill",
                table: "schema_info",
                keyColumn: "id",
                keyValue: 1,
                column: "version",
                value: 3);

            migrationBuilder.CreateIndex(
                name: "words_normalized_type",
                schema: "runninghill",
                table: "words",
                columns: new[] { "normalized_word", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "words_normalized_type",
                schema: "runninghill",
                table: "words");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_word",
                schema: "runninghill",
                table: "words",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldCollation: "C");

            migrationBuilder.UpdateData(
                schema: "runninghill",
                table: "schema_info",
                keyColumn: "id",
                keyValue: 1,
                column: "version",
                value: 2);

            migrationBuilder.CreateIndex(
                name: "words_word_type",
                schema: "runninghill",
                table: "words",
                columns: new[] { "word", "type" },
                unique: true);
        }
    }
}
