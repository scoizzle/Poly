#nullable enable

using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Generated;

public class LibraryDbContext : DbContext, Poly.DomainModeling.Runtime.IStoragePort
{
    private readonly HashSet<string> _pendingKeys = new HashSet<string>();
    public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }
    public DbSet<Book> Books
    {
        get => Set<Book>();
    }
    public DbSet<Patron> Patrons
    {
        get => Set<Patron>();
    }
    public DbSet<Loan> Loans
    {
        get => Set<Loan>();
    }
    public DbSet<Fine> Fines
    {
        get => Set<Fine>();
    }
    public DbSet<PremiumPatron> PremiumPatrons
    {
        get => Set<PremiumPatron>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(b => {
    b.ToTable("Books", t => {
    t.HasCheckConstraint("CK_pages", "pages >= 1 AND pages <= 10000");
});
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Title).HasColumnName("title").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Author).HasColumnName("author").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.ISBN).HasColumnName("isbn").HasColumnType("TEXT").IsRequired().HasMaxLength(17);
    b.Property(x => x.Pages).HasColumnName("pages").HasColumnType("INTEGER");
    b.Property(x => x.Genre).HasColumnName("genre").HasColumnType("INTEGER").HasConversion<int>().IsRequired();
    b.HasIndex(x => x.ISBN).IsUnique();
});
        modelBuilder.Entity<Patron>(b => {
    b.ToTable("Patrons", t => {
    t.HasCheckConstraint("CK_maxItems", "maxItems >= 0 AND maxItems <= 20");
});
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT").IsRequired().HasMaxLength(256);
    b.Property(x => x.MemberSince).HasColumnName("memberSince").HasColumnType("TEXT");
    b.Property(x => x.Status).HasColumnName("status").HasColumnType("INTEGER").HasConversion<int>().IsRequired();
    b.Property(x => x.MaxItems).HasColumnName("maxItems").HasColumnType("INTEGER");
    b.Property(x => x.CurrentBorrowCount).HasColumnName("currentBorrowCount").HasColumnType("INTEGER");
    b.Property(x => x.OutstandingFines).HasColumnName("outstandingFines").HasColumnType("INTEGER");
    b.HasIndex(x => x.Email).IsUnique();
    b.Metadata.FindNavigation(nameof(Patron.Loans))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    b.Metadata.FindNavigation(nameof(Patron.Fines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
});
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
        modelBuilder.Entity<Fine>(b => {
    b.ToTable("Fines");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("INTEGER");
    b.Property(x => x.Reason).HasColumnName("reason").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.DateIssued).HasColumnName("dateIssued").HasColumnType("TEXT");
    b.Property(x => x.Paid).HasColumnName("paid").HasColumnType("INTEGER");
});
        modelBuilder.Entity<PremiumPatron>(b => {
    b.ToTable("PremiumPatrons");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT").IsRequired().HasMaxLength(256);
    b.Property(x => x.RewardPoints).HasColumnName("rewardPoints").HasColumnType("INTEGER");
    b.Property(x => x.Tier).HasColumnName("tier").HasColumnType("INTEGER").HasConversion<int>().IsRequired();
    b.Property(x => x.PriorityAccess).HasColumnName("priorityAccess").HasColumnType("INTEGER");
    b.HasIndex(x => x.Email).IsUnique();
});
    }
    public bool Exists(string entity, string property, string value)
    {
        if (this._pendingKeys.Contains(entity + "\u001f" + property + "\u001f" + value))
        {
            return true;
        }
        if (entity == "Book")
        {
            if (property == "ISBN")
            {
                return this.Books.Any(e => e.ISBN == value);
            }
            return false;
        }
        if (entity == "Patron")
        {
            if (property == "Email")
            {
                return this.Patrons.Any(e => e.Email == value);
            }
            return false;
        }
        if (entity == "PremiumPatron")
        {
            if (property == "Email")
            {
                return this.PremiumPatrons.Any(e => e.Email == value);
            }
            return false;
        }
        return false;
    }
    public void Record(string entity, string property, string value)
    {
        this._pendingKeys.Add(entity + "\u001f" + property + "\u001f" + value);
    }
    public void Release(string entity, string property, string value)
    {
        this._pendingKeys.Remove(entity + "\u001f" + property + "\u001f" + value);
    }
}