# REST API Experiment — Generated Entities + EF Core + ASP.NET

**Date:** 2026-07-20
**Location:** `demo/Poly.RestApi/`
**DSL source:** `docs/experiments/examples/library-checkout.poly`
**C# generation:** `src/Poly.DslCompiler/` (reuses `DomainToCSharpExporter` + `CSharpGenerator`)

## What was proven

The experiment demonstrates that Poly-generated C# entities — with private constructors,
`IReadOnlyList<T>` collection navigations, lifecycle stages, stage guards, policy guards,
and `DomainResult<T>` action methods — can be used as a **real data layer** in an
ASP.NET Core Minimal API backed by Entity Framework Core.

| Layer | What it proved |
|-------|---------------|
| **DSL → C#** | The DslCompiler produces valid, compilable C# with zero warnings |
| **EF Core InMemory** | Generated entities work with EF: private constructors, backing fields, navigation cycles |
| **Minimal API** | `DomainResult<T>` maps cleanly to HTTP via switch expressions |
| **Constraint validation** | `Create()` factories reject invalid input at the boundary |
| **Stage guards** | Actions block with HTTP 409 when called in the wrong lifecycle stage |
| **Policy guards** | Named policies (`AtLimit`, `GoodStanding`) produce descriptive failure messages |
| **Subscription wiring** | `when Rel Stage { effects }` generates correct cross-entity notification |

## Project structure

```
demo/Poly.RestApi/
  Poly.RestApi.csproj       — Web SDK + EF Core InMemory
  Program.cs                — Minimal API endpoints, seed data, DTOs
  demo.http                 — Request file for VS Code REST Client
  Data/
    LibraryDbContext.cs      — EF Core mapping for generated entities
  _all.cs                   — Generated C# (copied from DslCompiler output)
```

## How to regenerate entities

```bash
# 1. Build the compiler
dotnet build src/Poly.DslCompiler/Poly.DslCompiler.csproj

# 2. Generate C# from the .poly DSL
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj \
  docs/experiments/examples/library-checkout.poly /tmp/library-api/

# 3. Copy into the experiment project
cp /tmp/library-api/_all.cs demo/Poly.RestApi/_all.cs

# 4. Build and run
dotnet build demo/Poly.RestApi/Poly.RestApi.csproj      # verify zero warnings
dotnet run --project demo/Poly.RestApi --urls "http://localhost:5201"
```

## How to test

Open `demo/Poly.RestApi/demo.http` in VS Code and click "Send Request" above any
request block. The expected flow:

```
GET  /api/books                          → 200, 3 books seeded
POST /api/patrons/bob/checkout (Dune)    → 200, Loan returned
POST /api/patrons/bob/checkout (N'man)   → 200, Loan returned
POST /api/patrons/bob/suspend            → 200, { status: "suspended" }
POST /api/patrons/bob/checkout (Gödel)   → 409, "'CheckOut' requires stage 'Active'…"
POST /api/patrons/bob/reinstate          → 500 (Q3′ policy not yet evaluable)
GET  /api/loans                          → 200, 2 loans
```

## Architecture patterns discovered

### 1. EF Core needs a private parameterless constructor

Generated entities have a private parameterized constructor used by the `Create` factory.
EF Core cannot bind camelCase constructor parameters (`isbn`) to PascalCase properties
(`ISBN`). **Fix:** emit a private parameterless constructor that EF Core uses for
materialization, and let property setters (also private) do the binding.

```csharp
private Book() { /* EF materialization */ }          // <-- emitted for EF
private Book(string author, Genre genre, string isbn, long pages, string title)  // <-- used by Create
{
    this.Author = author;
    this.Genre = genre;
    this.ISBN = isbn;
    // ...
}
```

Project-level `CS8618` suppression is needed because the parameterless constructor
leaves non-nullable reference-type properties uninitialized:

```xml
<NoWarn>$(NoWarn);CS8618</NoWarn>
```

### 2. Collection navigations need property access mode

Generated entities expose `IReadOnlyList<T>` for collection navigations backed by
`private List<T>` fields. EF must be told to write to the backing field:

```csharp
p.Metadata.FindNavigation(nameof(Patron.Loans))!
    .SetPropertyAccessMode(PropertyAccessMode.Field);
p.Metadata.FindNavigation(nameof(Patron.Fines))!
    .SetPropertyAccessMode(PropertyAccessMode.Field);
```

### 3. Navigation properties should not be configured as scalar

Don't call `.Property()` on reference navigations (e.g. `Loan.Book`, `Loan.Borrower`).
EF resolves them automatically from the entity model. Treat as **navigation** not **scalar**.

### 4. JSON cycle handling for bidirectional navs

`Loan → Borrower → Loans → …` creates infinite cycles in JSON serialization.
Required in `Program.cs`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
```

Also add `JsonStringEnumConverter` so enum types serialize as strings, not integers.

### 5. Factory methods return DomainResult<T>

The `Create` factory validates constraints and returns `DomainResult<T>`:

```csharp
// Before (unvalidated):
public static Book Create(...) => new Book(...);

// After (validated):
public static DomainResult<Book> Create(string author, Genre genre, string isbn, long pages, string title)
{
    if (string.IsNullOrEmpty(author))
        return DomainResult<Book>.Failure("'Author' is required.");
    if (isbn.Length < 10L)
        return DomainResult<Book>.Failure("'ISBN' must be at least 10 characters.");
    if (pages < 1L)
        return DomainResult<Book>.Failure("'Pages' must be >= 1.");
    if (string.IsNullOrEmpty(title))
        return DomainResult<Book>.Failure("'Title' is required.");
    return DomainResult<Book>.Success(new Book(author, genre, isbn, pages, title));
}
```

### 6. Action methods use DomainResult<T> uniformly

Every generated action returns either `DomainResult` (void) or `DomainResult<T>` (typed).
Consumers pattern-match:

```csharp
var result = patron.CheckOut(book);
return result switch {
    { IsSuccess: true, Value: var loan } => Results.Ok(loan),
    { ErrorMessage: [..] msg }           => Results.Conflict(new { error = msg }),
    _                                    => Results.StatusCode(500)
};
```

### 7. POST endpoints for custom entities need .Value unwrapping

Because `Create` returns `DomainResult<T>`, seed code and POST handlers must unwrap:

```csharp
var bookResult = Book.Create(dto.Author, dto.Genre, dto.ISBN, dto.Pages, dto.Title);
if (!bookResult.IsSuccess) return Results.Conflict(new { error = bookResult.ErrorMessage });
db.Books.Add(bookResult.Value);
```

## Runtime behavior

### Successful action (200)

```json
{
    "isDeleted": false,
    "checkedOutAt": "2026-07-20T20:32:16Z",
    "dueDate": "0001-01-01T00:00:00",
    "book": { "isbn": "978-0441172719", "title": "Dune", ... },
    "currentStage": 0
}
```

### Stage violation (409)

```json
{ "error": "'CheckOut' requires stage 'Active' on entity 'Patron'." }
```

### Policy violation (409)

```json
{ "error": "'CheckOut' blocked by policy 'AtLimit'." }
```

### Q3′ quantifier policy (500)

Policies using `any`, `all`, `none`, or `count` over collections are tracked in the
domain model but **cannot yet be compiled to standalone C#**. They throw
`NotSupportedException` at runtime:

```json
{
    "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    "title": "An error occurred",
    "status": 500,
    "detail": "Policy 'HasOverdueLoans' requires store-aware evaluation…"
}
```

This is expected — Q3′ evaluation requires an instance store (planned future phase).

### Constraint validation rejection (409)

```json
{ "error": "'ISBN' must be at least 10 characters." }
```

## Generated C# shape

### Entity types

Each DSL entity generates:
- An `enum {Name}Stage` for lifecycle stages
- A `class {Name}` with private parameterless + parameterized constructors
- Private property setters
- `IReadOnlyList<T>` for collection navigations (backed by `List<T>` fields)
- `DomainResult<T>` action methods with stage + policy guards
- `internal void` subscription handler methods
- `public static DomainResult<T> Create(...)` factory

### DomainResult infrastructure

Two record structs emitted once at the top of every generated file:

```csharp
public record DomainResult { ... }      // void actions
public record DomainResult<T> { ... }   // typed actions
```

Both expose `IsSuccess`, `ErrorMessage`, and static `Success()`/`Failure()` factories.

## Known limitations

| Limitation | Impact | Future plan |
|-----------|--------|-------------|
| Q3′ quantifiers (`any`/`all`/`none`/`count`) | Require store-aware runtime | Connect instance store to VM evaluation |
| EF `PropertyAccessMode.Field` must be set manually | Need per-navigation config in DbContext | Add `owned` navigation metadata for auto-config |
| No migration support | InMemory only for experiments | SQLite/SQL Server mapping would need additional config |
| `CS8618` suppression at project level | Hides genuine uninit warnings | Could use `required` modifier or `#nullable restore` per-type |
