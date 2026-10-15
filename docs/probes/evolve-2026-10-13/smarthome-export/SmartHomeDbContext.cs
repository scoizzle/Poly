#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class SmartHomeDbContext : DbContext
{
    public SmartHomeDbContext(DbContextOptions<SmartHomeDbContext> options) : base(options) { }
    public DbSet<Scene> Scenes
    {
        get => Set<Scene>();
    }
    public DbSet<Device> Devices
    {
        get => Set<Device>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scene>(b => {
    b.ToTable("Scenes");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.IsActive).HasColumnName("isActive").HasColumnType("INTEGER");
    b.Metadata.FindNavigation(nameof(Scene.Devices))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Device>(b => {
    b.ToTable("Devices", t => {
    t.HasCheckConstraint("CK_power", "power >= 0 AND power <= 3000");
    t.HasCheckConstraint("CK_maxLevel", "maxLevel >= 0 AND maxLevel <= 100");
}
);
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Power).HasColumnName("power").HasColumnType("INTEGER");
    b.Property(x => x.MaxLevel).HasColumnName("maxLevel").HasColumnType("INTEGER");
    b.Property(x => x.Draw).HasColumnName("draw").HasColumnType("INTEGER");
}
);
    }
}