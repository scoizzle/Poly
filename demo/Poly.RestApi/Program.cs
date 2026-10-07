#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Poly.Generated;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
}
);

builder.Services.AddDbContext<LibraryDbContext>(options => options.UseSqlite("Data Source=library.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAsync(db);
}

app.MapGet("/api/books", async (LibraryDbContext db) => await db.Books.ToListAsync());

app.MapGet("/api/books/{isbn}", async (string isbn, LibraryDbContext db) => (await db.Books.FindAsync(isbn) is Book book ? Results.Ok(book) : Results.NotFound()));

app.MapPost("/api/books", async (BookDto dto, LibraryDbContext db) => {
    var bookResult = Book.Create(dto.Author, dto.Genre, dto.ISBN, dto.Pages, dto.Title);
    if (!bookResult.IsSuccess)
    {
        return Results.Conflict(bookResult.ErrorMessage);
    }
    db.Books.Add(bookResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/books/", bookResult.Value.ISBN.ToString()), bookResult.Value);
}
);

app.MapGet("/api/patrons", async (LibraryDbContext db) => await db.Patrons.ToListAsync());

app.MapGet("/api/patrons/{email}", async (string email, LibraryDbContext db) => (await db.Patrons.FindAsync(email) is Patron patron ? Results.Ok(patron) : Results.NotFound()));

app.MapPost("/api/patrons", async (PatronDto dto, LibraryDbContext db) => {
    var patronResult = Patron.Create(dto.CurrentBorrowCount, dto.Email, dto.MaxItems, dto.Name, dto.OutstandingFines, Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (!patronResult.IsSuccess)
    {
        return Results.Conflict(patronResult.ErrorMessage);
    }
    db.Patrons.Add(patronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/patrons/", patronResult.Value.Email.ToString()), patronResult.Value);
}
);

app.MapGet("/api/premiumpatrons", async (LibraryDbContext db) => await db.PremiumPatrons.ToListAsync());

app.MapGet("/api/premiumpatrons/{email}", async (string email, LibraryDbContext db) => (await db.PremiumPatrons.FindAsync(email) is PremiumPatron premiumPatron ? Results.Ok(premiumPatron) : Results.NotFound()));

app.MapPost("/api/premiumpatrons", async (PremiumPatronDto dto, LibraryDbContext db) => {
    var premiumPatronResult = PremiumPatron.Create(dto.Email, dto.Name, dto.PriorityAccess, dto.RewardPoints);
    if (!premiumPatronResult.IsSuccess)
    {
        return Results.Conflict(premiumPatronResult.ErrorMessage);
    }
    db.PremiumPatrons.Add(premiumPatronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/premiumpatrons/", premiumPatronResult.Value.Email.ToString()), premiumPatronResult.Value);
}
);

app.MapGet("/api/patrons/{email}/loans", async (string email, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent == null)
    {
        return Results.NotFound();
    }
    await db.Entry(parent).Collection(e => e.Loans).LoadAsync();
    return Results.Ok(parent.Loans);
}
);

app.MapGet("/api/patrons/{email}/loans/{id}", async (string email, int id, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent == null)
    {
        return Results.NotFound();
    }
    await db.Entry(parent).Collection(e => e.Loans).LoadAsync();
    var child = parent.Loans.FirstOrDefault();
    if (child == null)
    {
        return Results.NotFound();
    }
    return Results.Ok(child);
}
);

app.MapGet("/api/patrons/{email}/fines", async (string email, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent == null)
    {
        return Results.NotFound();
    }
    await db.Entry(parent).Collection(e => e.Fines).LoadAsync();
    return Results.Ok(parent.Fines);
}
);

app.MapGet("/api/patrons/{email}/fines/{id}", async (string email, int id, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent == null)
    {
        return Results.NotFound();
    }
    await db.Entry(parent).Collection(e => e.Fines).LoadAsync();
    var child = parent.Fines.FirstOrDefault();
    if (child == null)
    {
        return Results.NotFound();
    }
    return Results.Ok(child);
}
);

app.MapPost("/api/patrons/{email}/checkout", async (string email, CheckOutDto dto, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var book = await db.Books.FindAsync(dto.bookId);
        if (book == null)
        {
            return Results.NotFound("Book not found");
        }
        var result = entity.CheckOut(book);
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/suspend", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.Suspend();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/closeaccount", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.CloseAccount();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/reinstate", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.Reinstate();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/loans/{id}/renew", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity == null)
    {
        return Results.NotFound("Patron not found");
    }
    var entity = await db.Loans.FindAsync(id);
    if (entity == null)
    {
        return Results.NotFound("Loan not found");
    }
    await db.Entry(parentEntity).Collection(e => e.Loans).LoadAsync();
    if (!parentEntity.Loans.Any(e => e == entity))
    {
        return Results.NotFound("Loan not found for this Patron");
    }
    try
    {
        var result = entity.Renew();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/loans/{id}/return", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity == null)
    {
        return Results.NotFound("Patron not found");
    }
    var entity = await db.Loans.FindAsync(id);
    if (entity == null)
    {
        return Results.NotFound("Loan not found");
    }
    await db.Entry(parentEntity).Collection(e => e.Loans).LoadAsync();
    if (!parentEntity.Loans.Any(e => e == entity))
    {
        return Results.NotFound("Loan not found for this Patron");
    }
    try
    {
        var result = entity.Return();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/fines/{id}/pay", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity == null)
    {
        return Results.NotFound("Patron not found");
    }
    var entity = await db.Fines.FindAsync(id);
    if (entity == null)
    {
        return Results.NotFound("Fine not found");
    }
    await db.Entry(parentEntity).Collection(e => e.Fines).LoadAsync();
    if (!parentEntity.Fines.Any(e => e == entity))
    {
        return Results.NotFound("Fine not found for this Patron");
    }
    try
    {
        var result = entity.Pay();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

app.MapPost("/api/patrons/{email}/fines/{id}/waive", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity == null)
    {
        return Results.NotFound("Patron not found");
    }
    var entity = await db.Fines.FindAsync(id);
    if (entity == null)
    {
        return Results.NotFound("Fine not found");
    }
    await db.Entry(parentEntity).Collection(e => e.Fines).LoadAsync();
    if (!parentEntity.Fines.Any(e => e == entity))
    {
        return Results.NotFound("Fine not found for this Patron");
    }
    try
    {
        var result = entity.Waive();
        await db.SaveChangesAsync();
        if (result.IsSuccess)
        {
            return Results.Ok("ok");
        }
        else
        {
            return Results.Conflict(result.ErrorMessage);
        }
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
}
);

static async Task SeedAsync(LibraryDbContext db)
{
    if (db == null)
    {
        return;
    }
    if (await db.Books.AnyAsync())
    {
        return;
    }
    var bookResult = Book.Create("Sample", Genre.Fiction, "XXXXXXXXXX", 1, "Sample");
    if (bookResult.IsSuccess)
    {
        db.Add(bookResult.Value);
    }
    var patronResult = Patron.Create(1, "user@test.com", 1, "Sample", 1, Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (patronResult.IsSuccess)
    {
        db.Add(patronResult.Value);
    }
    var premiumPatronResult = PremiumPatron.Create("user@test.com", "Sample", false, 1);
    if (premiumPatronResult.IsSuccess)
    {
        db.Add(premiumPatronResult.Value);
    }
    await db.SaveChangesAsync();
}


app.Run();


public record BookDto
{
    [Required]
    public string Author { get; init; } = default!;
    [EnumDataType(typeof(Genre))]
    public Genre Genre { get; init; }
    [MinLength(10)]
    [MaxLength(17)]
    public string ISBN { get; init; } = default!;
    [Range(1, 10000)]
    public long Pages { get; init; }
    [Required]
    public string Title { get; init; } = default!;
}

public record PatronDto
{
    public long CurrentBorrowCount { get; init; }
    [RegularExpression("^[^@]+@[^@]+$")]
    public string Email { get; init; } = default!;
    [Range(0, 20)]
    public long MaxItems { get; init; }
    [Required]
    public string Name { get; init; } = default!;
    public long OutstandingFines { get; init; }
}

public record PremiumPatronDto
{
    [RegularExpression("^[^@]+@[^@]+$")]
    public string Email { get; init; } = default!;
    [Required]
    public string Name { get; init; } = default!;
    public bool PriorityAccess { get; init; }
    public long RewardPoints { get; init; }
}

public record CheckOutDto
{
    public string bookId { get; init; } = default!;
}