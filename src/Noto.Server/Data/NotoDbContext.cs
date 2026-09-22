using Microsoft.EntityFrameworkCore;
using Noto.Shared.Models;

namespace Noto.Server.Data;

public class NotoDbContext : DbContext
{
    public NotoDbContext(DbContextOptions<NotoDbContext> options) : base(options) { }

    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<EmbeddingProvider> EmbeddingProviders => Set<EmbeddingProvider>();
    public DbSet<Embedding> Embeddings => Set<Embedding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Entity>(e =>
        {
            e.ToTable("entities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Type).HasColumnName("type").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Body).HasColumnName("body");
            e.Property(x => x.Meta).HasColumnName("meta").HasColumnType("jsonb");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");

            e.HasIndex(x => new { x.Type, x.CreatedAt }).HasDatabaseName("idx_entities_type_created")
                .IsDescending(false, true);
        });

        modelBuilder.Entity<Attachment>(a =>
        {
            a.ToTable("attachments");
            a.HasKey(x => x.Id);
            a.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            a.Property(x => x.EntityId).HasColumnName("entity_id");
            a.Property(x => x.Kind).HasColumnName("kind").IsRequired();
            a.Property(x => x.Filename).HasColumnName("filename");
            a.Property(x => x.StoragePath).HasColumnName("storage_path").IsRequired();
            a.Property(x => x.MimeType).HasColumnName("mime_type");
            a.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            a.HasOne(x => x.Entity).WithMany(e => e.Attachments)
                .HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Link>(l =>
        {
            l.ToTable("links");
            l.HasKey(x => x.Id);
            l.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            l.Property(x => x.FromId).HasColumnName("from_id");
            l.Property(x => x.ToId).HasColumnName("to_id");
            l.Property(x => x.Reason).HasColumnName("reason");
            l.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            l.HasOne(x => x.From).WithMany(e => e.LinksFrom)
                .HasForeignKey(x => x.FromId).OnDelete(DeleteBehavior.Cascade);
            l.HasOne(x => x.To).WithMany(e => e.LinksTo)
                .HasForeignKey(x => x.ToId).OnDelete(DeleteBehavior.Cascade);

            l.HasIndex(x => x.FromId).HasDatabaseName("idx_links_from");
            l.HasIndex(x => x.ToId).HasDatabaseName("idx_links_to");
        });

        modelBuilder.Entity<EmbeddingProvider>(ep =>
        {
            ep.ToTable("embedding_providers");
            ep.HasKey(x => x.Id);
            ep.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            ep.Property(x => x.Name).HasColumnName("name").IsRequired();
            ep.Property(x => x.ModelId).HasColumnName("model_id").IsRequired();
            ep.Property(x => x.Provider).HasColumnName("provider").IsRequired();
            ep.Property(x => x.Endpoint).HasColumnName("endpoint");
            ep.Property(x => x.ModelPath).HasColumnName("model_path");
            ep.Property(x => x.Dimensions).HasColumnName("dimensions").IsRequired();
            ep.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            ep.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
            ep.Property(x => x.Notes).HasColumnName("notes");
        });

        modelBuilder.Entity<Embedding>(emb =>
        {
            emb.ToTable("embeddings");
            emb.HasKey(x => x.Id);
            emb.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            emb.Property(x => x.EntityId).HasColumnName("entity_id");
            emb.Property(x => x.ProviderId).HasColumnName("provider_id");
            emb.Property(x => x.ChunkIndex).HasColumnName("chunk_index").HasDefaultValue(0);
            emb.Property(x => x.ChunkText).HasColumnName("chunk_text");
            emb.Property(x => x.Vector).HasColumnName("vector").HasColumnType("vector");
            emb.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

            emb.HasOne(x => x.Entity).WithMany(e => e.Embeddings)
                .HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
            emb.HasOne(x => x.EmbeddingProvider).WithMany(ep => ep.Embeddings)
                .HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);

            // Unique: one embedding per entity per provider per chunk
            emb.HasIndex(x => new { x.EntityId, x.ProviderId, x.ChunkIndex })
                .HasDatabaseName("idx_embeddings_entity_provider_chunk")
                .IsUnique();

            emb.HasIndex(x => x.ProviderId).HasDatabaseName("idx_embeddings_provider");
        });
    }
}
