#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class LibraryDbContext : DbContext
{
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
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(b => {
    b.ToTable("Books");
    b.HasKey(x => x.Isbn);
    b.Property(x => x.Isbn).HasColumnName("isbn").HasColumnType("TEXT");
    b.Property(x => x.Title).HasColumnName("title").HasColumnType("TEXT").IsRequired();
}
);
        modelBuilder.Entity<Patron>(b => {
    b.ToTable("Patrons");
    b.HasKey(x => x.Email);
    b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT");
    b.Property(x => x.LastOverdue).HasColumnName("lastOverdue").HasColumnType("TEXT");
    b.Metadata.FindNavigation(nameof(Patron.Loans))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    b.Metadata.FindNavigation(nameof(Patron.Fines))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Loan>(b => {
    b.ToTable("Loans");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.DueDate).HasColumnName("dueDate").HasColumnType("TEXT");
    b.Property(x => x.ReturnedOn).HasColumnName("returnedOn").HasColumnType("TEXT");
    b.Property(x => x.Status).HasColumnName("status").HasColumnType("TEXT");
}
);
        modelBuilder.Entity<Fine>(b => {
    b.ToTable("Fines", t => {
    t.HasCheckConstraint("CK_amount", "amount >= 0");
}
);
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("INTEGER");
    b.Property(x => x.Reason).HasColumnName("reason").HasColumnType("TEXT");
}
);
    }
}