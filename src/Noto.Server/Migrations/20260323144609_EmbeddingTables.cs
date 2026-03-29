using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Noto.Server.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding",
                table: "entities");

            migrationBuilder.CreateTable(
                name: "embedding_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "embeddings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vector = table.Column<Vector>(type: "vector", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embeddings", x => x.id);
                    table.ForeignKey(
                        name: "FK_embeddings_embedding_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "embedding_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_embeddings_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_embeddings_entity_provider",
                table: "embeddings",
                columns: new[] { "entity_id", "provider_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_embeddings_provider",
                table: "embeddings",
                column: "provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embeddings");

            migrationBuilder.DropTable(
                name: "embedding_providers");

            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "entities",
                type: "vector(1536)",
                nullable: true);
        }
    }
}
