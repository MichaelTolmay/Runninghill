using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runninghill.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schema_info",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_info", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sentences",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    request_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    word_ids = table.Column<string>(type: "TEXT", maxLength: 1050, nullable: false),
                    text = table.Column<string>(type: "TEXT", maxLength: 4050, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "words",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    word = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_words", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "schema_info",
                columns: new[] { "id", "version" },
                values: new object[] { 1, 2 });

            migrationBuilder.CreateIndex(
                name: "sentences_request_id",
                table: "sentences",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "words_type_id",
                table: "words",
                columns: new[] { "type", "id" });

            migrationBuilder.CreateIndex(
                name: "words_word_type",
                table: "words",
                columns: new[] { "word", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schema_info");

            migrationBuilder.DropTable(
                name: "sentences");

            migrationBuilder.DropTable(
                name: "words");
        }
    }
}
