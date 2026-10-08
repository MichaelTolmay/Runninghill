using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Runninghill.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "runninghill");

            migrationBuilder.CreateTable(
                name: "schema_info",
                schema: "runninghill",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schema_info", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sentences",
                schema: "runninghill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    word_ids = table.Column<long[]>(type: "bigint[]", nullable: false),
                    text = table.Column<string>(type: "character varying(4050)", maxLength: 4050, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "words",
                schema: "runninghill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_words", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "runninghill",
                table: "schema_info",
                columns: new[] { "id", "version" },
                values: new object[] { 1, 2 });

            migrationBuilder.CreateIndex(
                name: "sentences_request_id",
                schema: "runninghill",
                table: "sentences",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "words_type_id",
                schema: "runninghill",
                table: "words",
                columns: new[] { "type", "id" });

            migrationBuilder.CreateIndex(
                name: "words_word_type",
                schema: "runninghill",
                table: "words",
                columns: new[] { "word", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schema_info",
                schema: "runninghill");

            migrationBuilder.DropTable(
                name: "sentences",
                schema: "runninghill");

            migrationBuilder.DropTable(
                name: "words",
                schema: "runninghill");
        }
    }
}
