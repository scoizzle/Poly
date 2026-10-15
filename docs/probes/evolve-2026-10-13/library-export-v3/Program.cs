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
    var bookResult = Book.Create(dto.Isbn, dto.Title);
    if (!bookResult.IsSuccess)
    {
        return Results.Conflict(bookResult.ErrorMessage);
    }
    db.Books.Add(bookResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/books/", bookResult.Value.Isbn.ToString()), bookResult.Value);
}
);

app.MapGet("/api/patrons", async (LibraryDbContext db) => await db.Patrons.ToListAsync());

app.MapGet("/api/patrons/{email}", async (string email, LibraryDbContext db) => (await db.Patrons.FindAsync(email) is Patron patron ? Results.Ok(patron) : Results.NotFound()));

app.MapPost("/api/patrons", async (PatronDto dto, LibraryDbContext db) => {
    var patronResult = Patron.Create(dto.Email, dto.LastOverdue, dto.Name, dto.Status, Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (!patronResult.IsSuccess)
    {
        return Results.Conflict(patronResult.ErrorMessage);
    }
    db.Patrons.Add(patronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created(string.Concat("/api/patrons/", patronResult.Value.Email.ToString()), patronResult.Value);
}
);

app.MapGet("/api/fines", async (LibraryDbContext db) => await db.Fines.ToListAsync());

app.MapGet("/api/fines/{id}", async (int id, LibraryDbContext db) => (await db.Fines.FindAsync(id) is Fine fine ? Results.Ok(fine) : Results.NotFound()));

app.MapPost("/api/fines", async (FineDto dto, LibraryDbContext db) => {
    var fineResult = Fine.Create(dto.Amount, dto.Reason);
    if (!fineResult.IsSuccess)
    {
        return Results.Conflict(fineResult.ErrorMessage);
    }
    db.Fines.Add(fineResult.Value);
    await db.SaveChangesAsync();
    return Results.Ok(fineResult.Value);
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

app.MapPost("/api/patrons/{email}/renewallactive", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.RenewAllActive();
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

app.MapPost("/api/patrons/{email}/renewandtag", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.RenewAndTag();
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

app.MapPost("/api/patrons/{email}/notifypatron", async (string email, NotifyPatronDto dto, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity == null)
    {
        return Results.NotFound("Patron not found");
    }
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();
    try
    {
        var result = entity.NotifyPatron(dto.message);
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

app.MapPost("/api/patrons/{email}/loans/{id}/markoverdue", async (string email, int id, LibraryDbContext db) => {
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
        var result = entity.MarkOverdue();
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
    var bookResult = Book.Create("Sample", "Sample");
    if (bookResult.IsSuccess)
    {
        db.Add(bookResult.Value);
    }
    var patronResult = Patron.Create("user@test.com", DateOnly.FromDateTime(DateTime.UtcNow), "Sample", "Sample", Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (patronResult.IsSuccess)
    {
        db.Add(patronResult.Value);
    }
    var fineResult = Fine.Create(1, "Sample");
    if (fineResult.IsSuccess)
    {
        db.Add(fineResult.Value);
    }
    await db.SaveChangesAsync();
}


app.Run();


public record BookDto
{
    public string Isbn { get; init; } = default!;
    [Required]
    public string Title { get; init; } = default!;
}

public record PatronDto
{
    public string Email { get; init; } = default!;
    public DateOnly LastOverdue { get; init; }
    [Required]
    public string Name { get; init; } = default!;
    public string Status { get; init; } = default!;
}

public record FineDto
{
    [Range(0, 9.223372036854776E+18)]
    public long Amount { get; init; }
    public string Reason { get; init; } = default!;
}

public record CheckOutDto
{
    public string bookId { get; init; } = default!;
}

public record NotifyPatronDto
{
    public Message message { get; init; } = default!;
}