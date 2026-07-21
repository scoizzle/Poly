#nullable enable
using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class LibraryDbContext : DbContext {
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Patron> Patrons => Set<Patron>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Fine> Fines => Set<Fine>();
    public DbSet<PremiumPatron> PremiumPatrons => Set<PremiumPatron>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        // ── Book ─────────────────────────────────────────────────
        modelBuilder.Entity<Book>(b => {
            b.HasKey(x => x.ISBN);
            b.Property(x => x.ISBN).HasMaxLength(17);
            b.Property(x => x.Title).IsRequired();
            b.Property(x => x.Author).IsRequired();
        });

        // ── Patron ─────────────────────────────────────────────────
        modelBuilder.Entity<Patron>(b => {
            b.HasKey(x => x.Email);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.Name).IsRequired();
            b.Metadata.FindNavigation(nameof(Patron.Loans))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
            b.Metadata.FindNavigation(nameof(Patron.Fines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // ── Loan ─────────────────────────────────────────────────
        modelBuilder.Entity<Loan>(b => {
            b.Property<int>("Id");
            b.HasKey("Id");
        });

        // ── Fine ─────────────────────────────────────────────────
        modelBuilder.Entity<Fine>(b => {
            b.Property<int>("Id");
            b.HasKey("Id");
        });

        // ── PremiumPatron ─────────────────────────────────────────────────
        modelBuilder.Entity<PremiumPatron>(b => {
            b.HasKey(x => x.Email);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.Name).IsRequired();
        });

    }
}