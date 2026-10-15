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
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Metadata.FindNavigation(nameof(Patron.Loans))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Loan>(b => {
    b.ToTable("Loans");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.DueDate).HasColumnName("dueDate").HasColumnType("TEXT");
}
);
    }
}