#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }
    public DbSet<Meter> Meters
    {
        get => Set<Meter>();
    }
    public DbSet<Reading> Readings
    {
        get => Set<Reading>();
    }
    public DbSet<Note> Notes
    {
        get => Set<Note>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meter>(b => {
    b.ToTable("Meters", t => {
    t.HasCheckConstraint("CK_rate", "rate >= 0");
    t.HasCheckConstraint("CK_peakRate", "peakRate >= 0");
    t.HasCheckConstraint("CK_offPeakRate", "offPeakRate >= 0");
}
);
    b.HasKey(x => x.Serial);
    b.Property(x => x.Serial).HasColumnName("serial").HasColumnType("TEXT");
    b.Property(x => x.Rate).HasColumnName("rate").HasColumnType("INTEGER");
    b.Property(x => x.PeakRate).HasColumnName("peakRate").HasColumnType("INTEGER");
    b.Property(x => x.OffPeakRate).HasColumnName("offPeakRate").HasColumnType("INTEGER");
    b.Metadata.FindNavigation(nameof(Meter.Readings))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Reading>(b => {
    b.ToTable("Readings", t => {
    t.HasCheckConstraint("CK_usage", "usage >= 0");
}
);
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Usage).HasColumnName("usage").HasColumnType("INTEGER");
    b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("INTEGER");
    b.Property(x => x.RecordedAt).HasColumnName("recordedAt").HasColumnType("TEXT");
    b.Property(x => x.CreatedAt).HasColumnName("createdAt").HasColumnType("TEXT");
    b.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT");
    b.Metadata.FindNavigation(nameof(Reading.Notes))!.SetPropertyAccessMode(PropertyAccessMode.Field);
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