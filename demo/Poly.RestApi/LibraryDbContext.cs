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
            b.ToTable("Books");
            b.HasKey(x => x.ISBN);
            b.Property(x => x.ISBN).HasColumnName("isbn").HasColumnType("TEXT").HasMaxLength(17);
            b.Property(x => x.Title).HasColumnName("title").HasColumnType("TEXT").IsRequired();
            b.Property(x => x.Author).HasColumnName("author").HasColumnType("TEXT").IsRequired();
            b.Property(x => x.Pages).HasColumnName("pages").HasColumnType("INTEGER");
            b.Property(x => x.Genre).HasColumnName("genre").HasColumnType("Genre");
        });

        // ── Patron ─────────────────────────────────────────────────
        modelBuilder.Entity<Patron>(b => {
            b.ToTable("Patrons");
            b.HasKey(x => x.Email);
            b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT").HasMaxLength(256);
            b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
            b.Property(x => x.MemberSince).HasColumnName("memberSince").HasColumnType("TEXT");
            b.Property(x => x.Status).HasColumnName("status").HasColumnType("PatronStatus");
            b.Property(x => x.MaxItems).HasColumnName("maxItems").HasColumnType("INTEGER");
            b.Property(x => x.CurrentBorrowCount).HasColumnName("currentBorrowCount").HasColumnType("INTEGER");
            b.Property(x => x.OutstandingFines).HasColumnName("outstandingFines").HasColumnType("INTEGER");
            b.Metadata.FindNavigation(nameof(Patron.Loans))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
            b.Metadata.FindNavigation(nameof(Patron.Fines))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // ── Loan ─────────────────────────────────────────────────
        modelBuilder.Entity<Loan>(b => {
            b.ToTable("Loans");
            b.Property<int>("Id");
            b.HasKey("Id");
            b.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT");
            b.Property(x => x.CheckedOutAt).HasColumnName("checkedOutAt").HasColumnType("TEXT");
            b.Property(x => x.DueDate).HasColumnName("dueDate").HasColumnType("TEXT");
            b.Property(x => x.ReturnedAt).HasColumnName("returnedAt").HasColumnType("TEXT");
            b.Property(x => x.TimesRenewed).HasColumnName("timesRenewed").HasColumnType("INTEGER");
        });

        // ── Fine ─────────────────────────────────────────────────
        modelBuilder.Entity<Fine>(b => {
            b.ToTable("Fines");
            b.Property<int>("Id");
            b.HasKey("Id");
            b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("INTEGER");
            b.Property(x => x.Reason).HasColumnName("reason").HasColumnType("TEXT");
            b.Property(x => x.DateIssued).HasColumnName("dateIssued").HasColumnType("TEXT");
            b.Property(x => x.Paid).HasColumnName("paid").HasColumnType("INTEGER");
        });

        // ── PremiumPatron ─────────────────────────────────────────────────
        modelBuilder.Entity<PremiumPatron>(b => {
            b.ToTable("PremiumPatrons");
            b.HasKey(x => x.Email);
            b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT").HasMaxLength(256);
            b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
            b.Property(x => x.RewardPoints).HasColumnName("rewardPoints").HasColumnType("INTEGER");
            b.Property(x => x.Tier).HasColumnName("tier").HasColumnType("PremiumTier");
            b.Property(x => x.PriorityAccess).HasColumnName("priorityAccess").HasColumnType("INTEGER");
        });

    }
}