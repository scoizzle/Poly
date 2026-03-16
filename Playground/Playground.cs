var original = new Test("Original");
// Prints:
// Constructing Test with name 'Original'
// Setting Name to 'Original'

var copy = original with { Name = "Copy" };
// Prints:
// Setting Name to 'Original'
// Setting Name to 'Copy'

record Test
{
    public Test(string name)
    {
        Console.WriteLine($"Constructing Test with name '{name}'");
        Name = name;
    }
    
    public string Name { get; init { Console.WriteLine($"Setting Name to '{value}'"); field = value; } }
}

// #:property PublishAot=false
// #:package Microsoft.EntityFrameworkCore@10.0.3
// #:package Microsoft.EntityFrameworkCore.Design@10.0.3
// #:package Microsoft.EntityFrameworkCore.SQLite@10.0.3

// using Microsoft.EntityFrameworkCore;
// using Microsoft.EntityFrameworkCore.ValueGeneration;


// using var context = new AppDbContext();
// await context.Database.EnsureCreatedAsync();

// context.People.Add(new Person("John", "Doe"));
// context.People.Add(new Person("Kim", "Doe"));
// context.People.Add(new Person("John", "Bush"));
// context.People.Add(new Person("Lin", "Monroe"));
// context.SaveChanges();

// await foreach (var p in context.People) {
//     Console.WriteLine($"{p.FirstName} {p.LastName}");
// }

// // var person = await context.People.FirstAsync(person => person.FirstName == "John");

// // person.UpdateLastName("Smith");
// // context.SaveChanges();

// var doeIndividuals = context.People.Where(p => p.LastName == "Doe").Select(e => new { e.FirstName });

// var enumerable = doeIndividuals.AsEnumerable();
// var enumerator = enumerable.GetEnumerator();

// while (enumerator.MoveNext()) {
//     var person = enumerator.Current;
//     Console.WriteLine($"{person.FirstName} Doe");
// }

// class Person {
//     public Person(string firstName, string lastName)
//     {
//         FirstName = firstName;
//         LastName = lastName;
//     }

//     public string FirstName { get; private set; }
//     public string LastName { get; private set; }

//     public void UpdateLastName(string newLastName)
//     {
//         ArgumentException.ThrowIfNullOrWhiteSpace(newLastName);
//         LastName = newLastName;
//     }
// }

// class AppDbContext : DbContext {
//     public DbSet<Person> People { get; set; } = null!;

//     protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//     {
//         optionsBuilder.UseSqlite("Data Source=app.db");
//         optionsBuilder.LogTo(Console.WriteLine);
//         optionsBuilder.EnableSensitiveDataLogging();
//     }

//     protected override void OnModelCreating(ModelBuilder modelBuilder)
//     {
//         modelBuilder.Entity<Person>(entity => {
//             entity.Property<Guid>("_id").HasColumnName("Id").ValueGeneratedOnAdd().HasValueGenerator<GuidValueGenerator>();
//             entity.HasKey("_id");
//         });
//     }
// }