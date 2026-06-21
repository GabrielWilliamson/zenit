using Zenit.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Zenit.Data;

public class AppDbContext : DbContext
{
    public DbSet<TokenEntity> Tokens => Set<TokenEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenEntity>(entity =>
        {
            entity.ToTable("tokens");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                  .HasColumnName("id");

            entity.Property(e => e.AccessToken)
                  .HasColumnName("access_token")
                  .IsRequired();

            entity.Property(e => e.ExpiresAtUtc)
                  .HasColumnName("expires_at")
                  .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                  .HasColumnName("created_at")
                  .HasDefaultValueSql("NOW()");
        });
    }
}
