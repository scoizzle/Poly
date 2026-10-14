#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Poly.Generated;

public class RegistrationDbContext : DbContext
{
    public RegistrationDbContext(DbContextOptions<RegistrationDbContext> options) : base(options) { }
    public DbSet<Program> Programs
    {
        get => Set<Program>();
    }
    public DbSet<Student> Students
    {
        get => Set<Student>();
    }
    public DbSet<Course> Courses
    {
        get => Set<Course>();
    }
    public DbSet<Enrollment> Enrollments
    {
        get => Set<Enrollment>();
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Program>(b => {
    b.ToTable("Programs");
    b.HasKey(x => x.Code);
    b.Property(x => x.Code).HasColumnName("code").HasColumnType("TEXT").HasMaxLength(10);
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
}
);
        modelBuilder.Entity<Student>(b => {
    b.ToTable("Students");
    b.HasKey(x => x.StudentId);
    b.Property(x => x.StudentId).HasColumnName("studentId").HasColumnType("TEXT");
    b.Property(x => x.Email).HasColumnName("email").HasColumnType("TEXT");
    b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT").IsRequired();
    b.Metadata.FindNavigation(nameof(Student.Enrollments))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Course>(b => {
    b.ToTable("Courses", t => {
    t.HasCheckConstraint("CK_seats", "seats >= 0 AND seats <= 200");
    t.HasCheckConstraint("CK_credits", "credits >= 1 AND credits <= 6");
}
);
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.Code).HasColumnName("code").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Title).HasColumnName("title").HasColumnType("TEXT").IsRequired();
    b.Property(x => x.Seats).HasColumnName("seats").HasColumnType("INTEGER");
    b.Property(x => x.Credits).HasColumnName("credits").HasColumnType("INTEGER");
    b.Metadata.FindNavigation(nameof(Course.Enrollments))!.SetPropertyAccessMode(PropertyAccessMode.Field);
}
);
        modelBuilder.Entity<Enrollment>(b => {
    b.ToTable("Enrollments");
    b.Property<int>("Id");
    b.HasKey("Id");
    b.Property(x => x.EnrolledOn).HasColumnName("enrolledOn").HasColumnType("TEXT");
    b.Property(x => x.Grade).HasColumnName("grade").HasColumnType("TEXT");
}
);
    }
}