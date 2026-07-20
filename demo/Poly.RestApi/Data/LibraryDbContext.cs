using Microsoft.EntityFrameworkCore;

namespace Poly.RestApi.Data;

/// <summary>
/// EF Core DbContext for the generated Library domain entities.
///
/// The generated entity types use:
///   • Private constructors with property-matching parameters
///   • IReadOnlyList&lt;T&gt; for collection navigations (backed by List&lt;T&gt; fields)
///   • Private setters on all properties
///   • Static Create factory methods
///
/// This context configures EF Core to work with all of the above.
/// </summary>
public class LibraryDbContext : DbContext {
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Patron> Patrons => Set<Patron>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Fine> Fines => Set<Fine>();
    public DbSet<PremiumPatron> PremiumPatrons => Set<PremiumPatron>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        // ── Book ────────────────────────────────────────────────
        // ISBN is marked unique in the DSL — use as natural key.
        modelBuilder.Entity<Book>(b => {
            b.HasKey(x => x.ISBN);
            b.Property(x => x.ISBN).HasMaxLength(17);
            b.Property(x => x.Title).IsRequired();
            b.Property(x => x.Author).IsRequired();
        });

        // ── Patron ──────────────────────────────────────────────
        // Email is marked unique in the DSL — use as natural key.
        modelBuilder.Entity<Patron>(p => {
            p.HasKey(x => x.Email);
            p.Property(x => x.Email).HasMaxLength(256);
            p.Property(x => x.Name).IsRequired();

            // Collection navigations are backed by List<T> fields:
            //   private List<Loan> _loans;
            //   public  IReadOnlyList<Loan> Loans { get => _loans; }
            // Tell EF to write to the backing field directly.
            p.Metadata.FindNavigation(nameof(Patron.Loans))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
            p.Metadata.FindNavigation(nameof(Patron.Fines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // ── Loan ────────────────────────────────────────────────
        // No natural unique key — use auto-generated shadow key.
        modelBuilder.Entity<Loan>(l => {
            l.Property<int>("Id");
            l.HasKey("Id");

            // Book and Borrower are reference navigations, not scalar properties.
            // EF resolves them automatically from the entity model.
        });

        // ── Fine ────────────────────────────────────────────────
        // No natural unique key — use auto-generated shadow key.
        modelBuilder.Entity<Fine>(f => {
            f.Property<int>("Id");
            f.HasKey("Id");
            f.Property(x => x.Amount).IsRequired();
        });

        // ── PremiumPatron ───────────────────────────────────────
        // Email is marked unique — use as natural key.
        modelBuilder.Entity<PremiumPatron>(pp => {
            pp.HasKey(x => x.Email);
            pp.Property(x => x.Email).HasMaxLength(256);
            pp.Property(x => x.Name).IsRequired();
        });
    }
}