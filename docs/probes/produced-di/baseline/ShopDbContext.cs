#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options) { }
    public DbSet<Order> Orders
    {
        get => Set<Order>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b => {
    b.ToTable("Orders");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Number).HasColumnName("number").HasColumnType("TEXT");
    b.HasIndex(x => x.Number).IsUnique();
});
    }
}