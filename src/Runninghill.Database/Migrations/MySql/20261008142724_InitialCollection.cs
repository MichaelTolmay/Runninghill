using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Runninghill.Database.Migrations.MySql
{
    /// <inheritdoc />
    public partial class InitialCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "schema_info",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_info", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sentences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    request_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    word_ids = table.Column<string>(type: "varchar(1050)", maxLength: 1050, nullable: false),
                    text = table.Column<string>(type: "varchar(4050)", maxLength: 4050, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentences", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "words",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    word = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_words", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
