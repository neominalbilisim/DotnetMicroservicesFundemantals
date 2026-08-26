using Microsoft.EntityFrameworkCore;

namespace Neominal.Microservices.Template.Infrastructure;

/// <summary>
/// Senaryo: PostgreSQL bağlantısı ve basit bir CRUD akışı.
/// Not: Bu template'te EnsureCreated() kullanılmıştır (basitlik için).
/// Gerçek bir prod projede "dotnet ef migrations add" ile migration
/// yönetimi yapılması gerektiğini unutmayın (bkz. README).
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Price).HasColumnType("numeric(10,2)");
        });
    }
}
