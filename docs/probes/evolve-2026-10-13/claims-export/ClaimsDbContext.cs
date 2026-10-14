#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class ClaimsDbContext : DbContext
{
    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : base(options) { }
    public DbSet<Claimant> Claimants
    {
        get => Set<Claimant>();
    }
    public DbSet<Claim> Claims
    {
        get => Set<Claim>();
    }
    public DbSet<Assessor> Assessors
    {
        get => Set<Assessor>();
    }
    public DbSet<Note> Notes
    {
        get => Set<Note>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Claimant>(b => {
    b.ToTable("Claimants");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Metadata.FindNavigation(nameof(Claimant.Claims))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Claim>(b => {
    b.ToTable("Claims", t => {
    t.HasCheckConstraint("CK_amount", "amount >= 0");
}
);
    b.HasKey(x => x.RefNo);
    b.Property(x => x.RefNo).HasColumnName("refNo").HasColumnType("TEXT");
    b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("INTEGER");
    b.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT");
    b.Metadata.FindNavigation(nameof(Claim.Notes))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Assessor>(b => {
    b.ToTable("Assessors");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Metadata.FindNavigation(nameof(Assessor.Notes))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Note>(b => {
    b.ToTable("Notes");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Body).HasColumnName("body").HasColumnType("TEXT");
}
);
    }
}