#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Poly.Generated;

var builder = WebApplication.CreateBuilder(args);

// ── JSON: handle navigation cycles ──
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ── EF Core InMemory ──
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseInMemoryDatabase("Library"));

var app = builder.Build();

// ── Seed data ──
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    await SeedAsync(db);
}

// ── Book ──
app.MapGet("/api/books", async (LibraryDbContext db) =>
    await db.Books.ToListAsync());

app.MapGet("/api/books/{isbn}", async (string isbn, LibraryDbContext db) =>
    await db.Books.FindAsync(isbn) is Book book
        ? Results.Ok(book)
        : Results.NotFound());

app.MapPost("/api/books", async (BookDto dto, LibraryDbContext db) => {
    var bookResult = Book.Create(dto.Author, dto.Genre, dto.ISBN, dto.Pages, dto.Title);
    if (!bookResult.IsSuccess)
        return Results.Conflict(new { error = bookResult.ErrorMessage });
    db.Books.Add(bookResult.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/books/{bookResult.Value.ISBN}", bookResult.Value);
});

// ── Patron ──
app.MapGet("/api/patrons", async (LibraryDbContext db) =>
    await db.Patrons.ToListAsync());

app.MapGet("/api/patrons/{email}", async (string email, LibraryDbContext db) =>
    await db.Patrons.FindAsync(email) is Patron patron
        ? Results.Ok(patron)
        : Results.NotFound());

app.MapPost("/api/patrons", async (PatronDto dto, LibraryDbContext db) => {
    var patronResult = Patron.Create(dto.CurrentBorrowCount, dto.Email, dto.MaxItems, dto.Name, dto.OutstandingFines, Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (!patronResult.IsSuccess)
        return Results.Conflict(new { error = patronResult.ErrorMessage });
    db.Patrons.Add(patronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/patrons/{patronResult.Value.Email}", patronResult.Value);
});

// ── PremiumPatron ──
app.MapGet("/api/premiumpatrons", async (LibraryDbContext db) =>
    await db.PremiumPatrons.ToListAsync());

app.MapGet("/api/premiumpatrons/{email}", async (string email, LibraryDbContext db) =>
    await db.PremiumPatrons.FindAsync(email) is PremiumPatron premiumPatron
        ? Results.Ok(premiumPatron)
        : Results.NotFound());

app.MapPost("/api/premiumpatrons", async (PremiumPatronDto dto, LibraryDbContext db) => {
    var premiumPatronResult = PremiumPatron.Create(dto.Email, dto.Name, dto.PriorityAccess, dto.RewardPoints);
    if (!premiumPatronResult.IsSuccess)
        return Results.Conflict(new { error = premiumPatronResult.ErrorMessage });
    db.PremiumPatrons.Add(premiumPatronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/premiumpatrons/{premiumPatronResult.Value.Email}", premiumPatronResult.Value);
});

// ── Patron → Loan ──
app.MapGet("/api/patrons/{email}/loans", async (string email, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(parent).Collection(e => e.Loans).LoadAsync();
    return Results.Ok(parent.Loans);
});

app.MapGet("/api/patrons/{email}/loans/{id}", async (string email, int id, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(parent).Collection(e => e.Loans).LoadAsync();
    var child = parent.Loans.FirstOrDefault();
    if (child is null) return Results.NotFound(new { error = "Loan not found" });
    return Results.Ok(child);
});

// ── Patron → Fine ──
app.MapGet("/api/patrons/{email}/fines", async (string email, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(parent).Collection(e => e.Fines).LoadAsync();
    return Results.Ok(parent.Fines);
});

app.MapGet("/api/patrons/{email}/fines/{id}", async (string email, int id, LibraryDbContext db) => {
    var parent = await db.Patrons.FindAsync(email);
    if (parent is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(parent).Collection(e => e.Fines).LoadAsync();
    var child = parent.Fines.FirstOrDefault();
    if (child is null) return Results.NotFound(new { error = "Fine not found" });
    return Results.Ok(child);
});

// ── Action: CheckOut ──
app.MapPost("/api/patrons/{email}/checkout", async (string email, CheckOutDto dto, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();

    try {
        var book = await db.Books.FindAsync(dto.bookId);
        if (book is null) return Results.NotFound(new { error = "Book not found" });
        var result = entity.CheckOut(book);
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true, Value: var resultValue } => Results.Ok(resultValue),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Action: Suspend ──
app.MapPost("/api/patrons/{email}/suspend", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();

    try {
        var result = entity.Suspend();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Action: CloseAccount ──
app.MapPost("/api/patrons/{email}/closeaccount", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();

    try {
        var result = entity.CloseAccount();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Action: Reinstate ──
app.MapPost("/api/patrons/{email}/reinstate", async (string email, LibraryDbContext db) => {
    var entity = await db.Patrons.FindAsync(email);
    if (entity is null) return Results.NotFound(new { error = "Patron not found" });
    await db.Entry(entity).Collection(e => e.Loans).LoadAsync();
    await db.Entry(entity).Collection(e => e.Fines).LoadAsync();

    try {
        var result = entity.Reinstate();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Patron → Loan: Renew ──
app.MapPost("/api/patrons/{email}/loans/{id}/renew", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity is null) return Results.NotFound(new { error = "Patron not found" });
    var entity = await db.Loans.FindAsync(id);
    if (entity is null) return Results.NotFound(new { error = "Loan not found" });

    // Verify child belongs to parent
    await db.Entry(parentEntity).Collection(e => e.Loans).LoadAsync();
    if (!parentEntity.Loans.Any(e => e == entity))
        return Results.NotFound(new { error = "Loan not found for this Patron" });

    try {
        var result = entity.Renew();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Patron → Loan: Return ──
app.MapPost("/api/patrons/{email}/loans/{id}/return", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity is null) return Results.NotFound(new { error = "Patron not found" });
    var entity = await db.Loans.FindAsync(id);
    if (entity is null) return Results.NotFound(new { error = "Loan not found" });

    // Verify child belongs to parent
    await db.Entry(parentEntity).Collection(e => e.Loans).LoadAsync();
    if (!parentEntity.Loans.Any(e => e == entity))
        return Results.NotFound(new { error = "Loan not found for this Patron" });

    try {
        var result = entity.Return();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Patron → Fine: Pay ──
app.MapPost("/api/patrons/{email}/fines/{id}/pay", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity is null) return Results.NotFound(new { error = "Patron not found" });
    var entity = await db.Fines.FindAsync(id);
    if (entity is null) return Results.NotFound(new { error = "Fine not found" });

    // Verify child belongs to parent
    await db.Entry(parentEntity).Collection(e => e.Fines).LoadAsync();
    if (!parentEntity.Fines.Any(e => e == entity))
        return Results.NotFound(new { error = "Fine not found for this Patron" });

    try {
        var result = entity.Pay();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Patron → Fine: Waive ──
app.MapPost("/api/patrons/{email}/fines/{id}/waive", async (string email, int id, LibraryDbContext db) => {
    var parentEntity = await db.Patrons.FindAsync(email);
    if (parentEntity is null) return Results.NotFound(new { error = "Patron not found" });
    var entity = await db.Fines.FindAsync(id);
    if (entity is null) return Results.NotFound(new { error = "Fine not found" });

    // Verify child belongs to parent
    await db.Entry(parentEntity).Collection(e => e.Fines).LoadAsync();
    if (!parentEntity.Fines.Any(e => e == entity))
        return Results.NotFound(new { error = "Fine not found for this Patron" });

    try {
        var result = entity.Waive();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true } => Results.Ok(new { status = "ok" }),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ═══════════════════════════════════════════
//  Library API
//  Generated from Poly DSL
// ═══════════════════════════════════════════

//   GET  /api/books
//   GET  /api/books/{isbn}
//   POST /api/books
//   GET  /api/patrons
//   GET  /api/patrons/{email}
//   POST /api/patrons
//   POST /api/patrons/{email}/checkOut
//   POST /api/patrons/{email}/suspend
//   POST /api/patrons/{email}/closeAccount
//   POST /api/patrons/{email}/reinstate
//   GET  /api/patrons/{email}/loans
//   GET  /api/patrons/{email}/loans/{id}
//   POST /api/patrons/{email}/loans/{id}/renew
//   POST /api/patrons/{email}/loans/{id}/return
//   GET  /api/patrons/{email}/fines
//   GET  /api/patrons/{email}/fines/{id}
//   POST /api/patrons/{email}/fines/{id}/pay
//   POST /api/patrons/{email}/fines/{id}/waive
//   GET  /api/premiumpatrons
//   GET  /api/premiumpatrons/{email}
//   POST /api/premiumpatrons

app.Run();

static async Task SeedAsync(LibraryDbContext db) {
    if (db is null) return;
    if (await db.Books.AnyAsync()) return;

    var bookResult = Book.Create("Sample", Genre.Fiction, "XXXXXXXXXX", 1, "Sample");
    if (bookResult.IsSuccess)
        db.Add(bookResult.Value);

    var patronResult = Patron.Create(1, "user@test.com", 1, "Sample", 1, Enumerable.Empty<Loan>(), Enumerable.Empty<Fine>());
    if (patronResult.IsSuccess)
        db.Add(patronResult.Value);

    var premiumPatronResult = PremiumPatron.Create("user@test.com", "Sample", false, 1);
    if (premiumPatronResult.IsSuccess)
        db.Add(premiumPatronResult.Value);

    await db.SaveChangesAsync();
}

// ── DTOs ──
record BookDto(string Author, Genre Genre, string ISBN, long Pages, string Title);
record PatronDto(long CurrentBorrowCount, string Email, long MaxItems, string Name, long OutstandingFines);
record PremiumPatronDto(string Email, string Name, bool PriorityAccess, long RewardPoints);
record CheckOutDto(string bookId);