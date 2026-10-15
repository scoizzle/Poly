#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class CartDbContext : DbContext
{
    public CartDbContext(DbContextOptions<CartDbContext> options) : base(options) { }
    public DbSet<Cart> Carts
    {
        get => Set<Cart>();
    }
    public DbSet<Line> Lines
    {
        get => Set<Line>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cart>(b => {
    b.ToTable("Carts");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Total).HasColumnName("total").HasColumnType("INTEGER");
    b.Property(x => x.ItemCount).HasColumnName("itemCount").HasColumnType("INTEGER");
    b.Metadata.FindNavigation(nameof(Cart.Lines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Line>(b => {
    b.ToTable("Lines", t => {
    t.HasCheckConstraint("CK_price", "price >= 0");
    t.HasCheckConstraint("CK_qty", "qty >= 1");
}
);
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Sku).HasColumnName("sku").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Price).HasColumnName("price").HasColumnType("INTEGER");
    b.Property(x => x.Qty).HasColumnName("qty").HasColumnType("INTEGER");
}
);
    }
}