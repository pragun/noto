using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Noto.Server.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_embeddings_entity_provider",
                table: "embeddings");

            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "embeddings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "chunk_text",
                table: "embeddings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_embeddings_entity_provider_chunk",
                table: "embeddings",
                columns: new[] { "entity_id", "provider_id", "chunk_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_embeddings_entity_provider_chunk",
                table: "embeddings");

            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "embeddings");

            migrationBuilder.DropColumn(
                name: "chunk_text",
                table: "embeddings");

            migrationBuilder.CreateIndex(
                name: "idx_embeddings_entity_provider",
                table: "embeddings",
                columns: new[] { "entity_id", "provider_id" },
                unique: true);
        }
    }
}
