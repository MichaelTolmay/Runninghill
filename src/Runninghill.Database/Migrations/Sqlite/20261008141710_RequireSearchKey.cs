using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class RequireSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "words_word_type",
                table: "words");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_word",
                table: "words",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 160,
                oldNullable: true,
                oldCollation: "BINARY");

            migrationBuilder.UpdateData(
                table: "schema_info",
                keyColumn: "id",
                keyValue: 1,
                column: "version",
                value: 3);

            migrationBuilder.CreateIndex(
                name: "words_normalized_type",
                table: "words",
                columns: new[] { "normalized_word", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "words_normalized_type",
                table: "words");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_word",
                table: "words",
                type: "TEXT",
                maxLength: 160,
                nullable: true,
                collation: "BINARY",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 160,
                oldCollation: "BINARY");

            migrationBuilder.UpdateData(
                table: "schema_info",
                keyColumn: "id",
                keyValue: 1,
                column: "version",
                value: 2);

            migrationBuilder.CreateIndex(
                name: "words_word_type",
                table: "words",
                columns: new[] { "word", "type" },
                unique: true);
        }
    }
}
