using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.SqlServer
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
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

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
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(160)",
                oldMaxLength: 160,
                oldCollation: "Latin1_General_100_BIN2");

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
