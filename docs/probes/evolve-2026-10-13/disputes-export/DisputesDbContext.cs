#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class DisputesDbContext : DbContext
{
    public DisputesDbContext(DbContextOptions<DisputesDbContext> options) : base(options) { }
    public DbSet<Customer> Customers
    {
        get => Set<Customer>();
    }
    public DbSet<Dispute> Disputes
    {
        get => Set<Dispute>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b => {
    b.ToTable("Customers");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Metadata.FindNavigation(nameof(Customer.Disputes))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Dispute>(b => {
    b.ToTable("Disputes");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Reason).HasColumnName("reason").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Disputed).HasColumnName("disputed").HasColumnType("varchar");
    b.Property(x => x.Outcome).HasColumnName("outcome").HasColumnType("Outcome");
}
);
    }
}