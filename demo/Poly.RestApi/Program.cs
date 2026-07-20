// =============================================================================
// Library REST API — Experiment
//
// Demonstrates how Poly-generated C# entities integrate with:
//   • ASP.NET Core Minimal API     (request/response)
//   • EF Core InMemory             (storage)
//   • DomainResult<T> return type  (uniform action contract)
//
// The entity types (Book, Patron, Loan, Fine, PremiumPatron) and their action
// methods (CheckOut, Suspend, Reinstate, Pay, Waive) are 100% generated from
// the .poly DSL — see docs/experiments/examples/library-checkout.poly.
//
// Every generated action returns DomainResult (void actions) or
// DomainResult<T> (typed actions), letting callers pattern-match:
//
//   var result = patron.CheckOut(book);
//   return result switch {
//       { IsSuccess: true, Value: var loan } => Results.Ok(loan),
//       { ErrorMessage: [..] msg }           => Results.Conflict(new { error = msg })
//   };
//
// Stage guards and policy guards are automatic — no hand-written validation.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

using Poly.RestApi.Data;

var builder = WebApplication.CreateBuilder(args);

// ── JSON: handle navigation cycles (Loan → Borrower → Loans → …) ──
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// ── EF Core InMemory ──────────────────────────────────────────────
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseInMemoryDatabase("Library"));

var app = builder.Build();

// ── Seed data ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    await SeedAsync(db);
}

// ═══════════════════════════════════════════════════════════════════
// BOOK ENDPOINTS
// ═══════════════════════════════════════════════════════════════════

app.MapGet("/api/books", async (LibraryDbContext db) =>
    await db.Books.ToListAsync());

app.MapGet("/api/books/{isbn}", async (string isbn, LibraryDbContext db) =>
    await db.Books.FindAsync(isbn) is Book book
        ? Results.Ok(book)
        : Results.NotFound());

app.MapPost("/api/books", async (BookDto dto, LibraryDbContext db) => {
    var bookResult = Book.Create(dto.Author, dto.Genre, dto.ISBN, dto.Pages, dto.Title);
    if (!bookResult.IsSuccess) return Results.Conflict(new { error = bookResult.ErrorMessage });
    db.Books.Add(bookResult.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/books/{bookResult.Value.ISBN}", bookResult.Value);
});

// ═══════════════════════════════════════════════════════════════════
// PATRON ENDPOINTS
// ═══════════════════════════════════════════════════════════════════

app.MapGet("/api/patrons", async (LibraryDbContext db) =>
    await db.Patrons.ToListAsync());

app.MapGet("/api/patrons/{email}", async (string email, LibraryDbContext db) =>
    await db.Patrons.FindAsync(email) is Patron patron
        ? Results.Ok(patron)
        : Results.NotFound());

app.MapPost("/api/patrons", async (PatronDto dto, LibraryDbContext db) => {
    var patronResult = Patron.Create(
        dto.CurrentBorrowCount,
        dto.Email,
        dto.MaxItems,
        dto.Name,
        dto.OutstandingFines ?? 0,
        Enumerable.Empty<Loan>(),
        Enumerable.Empty<Fine>());
    if (!patronResult.IsSuccess) return Results.Conflict(new { error = patronResult.ErrorMessage });
    db.Patrons.Add(patronResult.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/patrons/{patronResult.Value.Email}", patronResult.Value);
});

// ── Action: CheckOut ─────────────────────────────────────────────
// Demonstrates DomainResult<T> — the generated action enforces stage
// guards ("must be Active") and policy guards ("must be GoodStanding",
// "not AtLimit", "not HasFines") automatically.
app.MapPost("/api/patrons/{email}/checkout", async (string email, CheckOutDto dto, LibraryDbContext db) => {
    var patron = await db.Patrons
        .Include(p => p.Loans)
        .FirstOrDefaultAsync(p => p.Email == email);
    if (patron is null) return Results.NotFound(new { error = "Patron not found" });

    var book = await db.Books.FindAsync(dto.ISBN);
    if (book is null) return Results.NotFound(new { error = "Book not found" });

    try {
        var result = patron.CheckOut(book);
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();

        return result switch {
            { IsSuccess: true, Value: var loan } => Results.Ok(loan),
            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
            _ => Results.StatusCode(500)
        };
    }
    catch (Exception ex) {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// ── Action: Suspend ──────────────────────────────────────────────
app.MapPost("/api/patrons/{email}/suspend", async (string email, LibraryDbContext db) => {
    var patron = await db.Patrons.FindAsync(email);
    if (patron is null) return Results.NotFound(new { error = "Patron not found" });

    var result = patron.Suspend();
    await db.SaveChangesAsync();

    return result switch {
        { IsSuccess: true } => Results.Ok(new { status = "suspended" }),
        { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
        _ => Results.StatusCode(500)
    };
});

// ── Action: Reinstate ────────────────────────────────────────────
app.MapPost("/api/patrons/{email}/reinstate", async (string email, LibraryDbContext db) => {
    var patron = await db.Patrons.FindAsync(email);
    if (patron is null) return Results.NotFound(new { error = "Patron not found" });

    var result = patron.Reinstate();
    await db.SaveChangesAsync();

    return result switch {
        { IsSuccess: true } => Results.Ok(new { status = "active" }),
        { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
        _ => Results.StatusCode(500)
    };
});

// ═══════════════════════════════════════════════════════════════════
// LOAN ENDPOINTS
// ═══════════════════════════════════════════════════════════════════

app.MapGet("/api/loans", async (LibraryDbContext db) =>
    await db.Loans.Include(l => l.Book).Include(l => l.Borrower).ToListAsync());

// ═══════════════════════════════════════════════════════════════════
// START
// ═══════════════════════════════════════════════════════════════════

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Library REST API — experiment");
Console.WriteLine("  Entities generated from library-checkout.poly");
Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("  Endpoints:");
Console.WriteLine("    GET  /api/books");
Console.WriteLine("    GET  /api/books/{isbn}");
Console.WriteLine("    POST /api/books");
Console.WriteLine("    GET  /api/patrons");
Console.WriteLine("    GET  /api/patrons/{email}");
Console.WriteLine("    POST /api/patrons");
Console.WriteLine("    POST /api/patrons/{email}/checkout   (action: CheckOut)");
Console.WriteLine("    POST /api/patrons/{email}/suspend    (action: Suspend)");
Console.WriteLine("    POST /api/patrons/{email}/reinstate  (action: Reinstate)");
Console.WriteLine("    GET  /api/loans");
Console.WriteLine();
Console.WriteLine("  Try: http://localhost:5001/api/books");
Console.WriteLine("       http://localhost:5001/api/patrons/alice@library.org/checkout");
Console.WriteLine();

app.Run();

// ═══════════════════════════════════════════════════════════════════
// SEED DATA
// ═══════════════════════════════════════════════════════════════════

static async Task SeedAsync(LibraryDbContext db) {
    if (await db.Books.AnyAsync()) return;

    // Books (Create now returns DomainResult<T> — unwrap with .Value)
    var dune = Book.Create("Frank Herbert", Genre.Fiction, "978-0441172719", 412, "Dune");
    var neuromancer = Book.Create("William Gibson", Genre.Fiction, "978-0441569595", 271, "Neuromancer");
    var godel = Book.Create("Douglas Hofstadter", Genre.NonFiction, "978-0465026562", 777, "Gödel, Escher, Bach");

    db.Books.AddRange(dune.Value!, neuromancer.Value!, godel.Value!);

    // Patrons (Create now returns DomainResult<T> — unwrap with .Value)
    var aliceResult = Patron.Create(
        currentBorrowCount: 0,
        email: "alice@library.org",
        maxItems: 5,
        name: "Alice Johnson",
        outstandingFines: 0,
        loans: Enumerable.Empty<Loan>(),
        fines: Enumerable.Empty<Fine>());

    var bobResult = Patron.Create(
        currentBorrowCount: 0,
        email: "bob@library.org",
        maxItems: 3,
        name: "Bob Smith",
        outstandingFines: 0,
        loans: Enumerable.Empty<Loan>(),
        fines: Enumerable.Empty<Fine>());

    db.Patrons.AddRange(aliceResult.Value!, bobResult.Value!);

    await db.SaveChangesAsync();

    Console.WriteLine($"  Seeded: {await db.Books.CountAsync()} books, {await db.Patrons.CountAsync()} patrons");
    Console.WriteLine();
}

// ═══════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════

record BookDto(string Author, Genre Genre, string ISBN, long Pages, string Title);
record PatronDto(string Name, string Email, long MaxItems, long CurrentBorrowCount, long? OutstandingFines);
record CheckOutDto(string ISBN);