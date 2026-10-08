# EF Core + Minimal API Code Generation Plan

**Date:** 2026-07-20  
**Status:** Draft — planning phase  
**Parent milestone:** Post-M2 expansion (generation is the engine)  
**Prerequisite:** Domain modeling current-state stability (see §0)  
**Experiment:** `demo/Poly.RestApi/` proved the concept end-to-end

---

## 0. Domain modeling current state

### What's done and stable

| Capability | Status | Tests |
|-----------|--------|-------|
| Entity → class + private ctors | ✅ Stable | 1478 total |
| `DomainResult`/`DomainResult<T>` infrastructure | ✅ Stable | 1478 total |
| `Create` factory with constraint validation | ✅ Stable | 1478 total |
| Action methods with stage + policy guards | ✅ Stable | 1478 total |
| Lifecycle stage enums + `CurrentStage` | ✅ Stable | 1478 total |
| Stage subscription wiring (cross-entity `internal void WhenXxx`) | ✅ Stable | 1478 total |
| Collection navs (`IReadOnlyList<T>` backed by `List<T>`) | ✅ Stable | 1478 total |
| Parameterless ctors (EF materialization) | ✅ Stable | 1478 total |
| `-> RetType` action return type syntax | ✅ Stable | 1478 total |
| Enum types with defaults | ✅ Stable | 1478 total |
| DSL parser + printer round-trip | ✅ Stable | 31 round-trip tests |
| `DomainExpression` lowering to Syntax AST | ✅ Stable | Coverage via policy tests |
| Effect lowering: assign, transition, conditional, create | ✅ Stable | Effect surface tests |

### Remaining domain-modeling gaps (not blockers for codegen)

These are tracked in existing plans but **do not block** EF + API codegen because the
codegen pipeline consumes the **already-stable entity/action/property model**:

| Gap | Plan reference | Impact on codegen |
|-----|---------------|-------------------|
| Q3′ quantifiers (`any`/`all`/`none`/`count`) → compile to C# | `dsl-query-surface.md` §15 | Policies using these throw `NotSupportedException` at runtime — codegen just emits the throw as-is |
| `create EntityType` standalone (not `create in Rel`) doesn't auto-add to collections | `effect-surface-completeness.md` E2 | Codegen emits `var x = X.Create(…)` — caller must add to collection |
| `TransitionRelationshipEffect` not executed at runtime | DMEFF005 | Codegen already emits a comment — not affected |
| `LinkRelationship`/`UnlinkRelationship` no DSL | `effect-surface-completeness.md` | No DSL surface to generate |

### Definition of "domain modeling ready for codegen expansion"

All criteria below are **already met** (no remaining work needed before starting API codegen):

- [x] Entity types compile to valid C# classes with properties, navs, actions, policies
- [x] Every action returns `DomainResult`/`DomainResult<T>` for uniform consumption
- [x] `Create` factories validate constraints + return `DomainResult<T>`
- [x] Stage guards + policy guards produce descriptive failure messages
- [x] EF Core can materialize instances (parameterless ctor)
- [x] Navigation properties use `IReadOnlyList<T>` / backing-field pattern
- [x] Subscription wiring generates correct cross-entity notification

---

## 1. Motivation

The `demo/Poly.RestApi/` experiment demonstrated that Poly-generated C# entities
work with ASP.NET Core + EF Core InMemory — but the **DbContext and API endpoints
were hand-written**. This took ~2 hours of manual coding and debugging.

**Goal:** Generate all of it from the `.poly` DSL so a user gets a working REST API
with zero hand-written infrastructure code.

**Named consumer:** "I wrote a `.poly` file. Running `dotnet poly compile --api`
gives me `LibraryDbContext.cs` + `Program.cs` that I can `dotnet run` to get a
fully functional REST API."

---

## 2. Architecture

### Pipeline context

```text
.poly DSL (library-checkout.poly)
  │
  ▼
DslCompiler                          ← already exists
  │  ├── Parse + Evolve
  │  └── DomainToCSharpExporter      ← already exists (entity types)
  │
  ├── Entity C# (_all.cs)            ← already exists
  ├── DbContext C# (NEW)             ← LibraryDbContext.cs
  └── Minimal API C# (NEW)           ← Program.cs
```

### Generation approach

**Do not** extend `DomainToCSharpExporter` into a monolithic "generates everything"
class. Instead, use **composable backends** that all consume the same `Domain` +
`AnalysisResult`:

```csharp
// Current:
var exporter = new DomainToCSharpExporter();
var typeDefs = exporter.Export(domain, analysis);
var csharp = new CSharpGenerator().Generate(typeDefs);

// New:
var exporter = new DomainToCSharpExporter();
var typeDefs = exporter.Export(domain, analysis);

var dbExporter = new DbContextExporter();
var dbDefs = dbExporter.Export(domain, analysis);

var apiExporter = new MinimalApiExporter();
var apiDefs = apiExporter.Export(domain, analysis);

// All combined into a single Program.cs + LibraryDbContext.cs
// Each backend defines its own Syntax AST nodes or uses CSharpGenerator directly.
```

All three backends share the same `CSharpGenerator` — they produce `TypeDefinitionNode`
trees that get formatted the same way.

---

## 3. Implementation status

| Phase | Status | Files |
|-------|--------|-------|
| **A — DbContext generation** | ✅ **Done** | `src/Poly.DslCompiler/DbContextGenerator.cs` |
| **B — Minimal API generation** | ✅ **Done** (MVP) | `src/Poly.DslCompiler/MinimalApiGenerator.cs` |
| **C — DslCompiler integration** | ✅ **Done** | `--mode db` and `--mode all` flags |
| **D — Storage backends** | ⏳ Pull when needed | |

### What ships today

Running `dotnet run --project src/Poly.DslCompiler -- --mode all domain.poly out/`
produces:

| File | Generated by | Contents |
|------|-------------|---------|
| `_all.cs` | `DomainToCSharpExporter` | Entity types + DomainResult + enums |
| `Book.cs` | `DomainToCSharpExporter` | Per-entity file |
| `LibraryDbContext.cs` | `DbContextGenerator` | EF Core InMemory DbContext |
| `Program.cs` | `MinimalApiGenerator` | Minimal API with CRUD + actions |

The generated API includes:
- Natural key routing for entities with `unique` constraint; shadow key `Id` otherwise
- CRUD endpoints with constraint validation (returns HTTP 409 on failure)
- Action endpoints with entity-param lookup from DB
- `DomainResult<T>` switch expressions → HTTP 200/409
- Stage guards + policy guards → HTTP 409 with descriptive messages
- JSON cycle handling + enum string serialization
- Seed data for seedable entities (those without required entity refs)
- DTO records for POST and action request bodies

### Phase A — DbContext generation

**Goal:** Generate `LibraryDbContext.cs` from entity metadata.

#### Input (from Domain)

For each entity:
- Properties with constraints (`HasKey`, `HasMaxLength`, `IsRequired`)
- Navigation properties (singular/collection, cardinality, ownership)
- Unique constraints
- Stage enums (not needed in DbContext)

#### Output

```csharp
public class LibraryDbContext : DbContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Patron> Patrons => Set<Patron>();
    // ...one per entity

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(b => {
            b.HasKey(x => x.ISBN);          // unique property → natural key
            b.Property(x => x.ISBN).HasMaxLength(17);
            b.Property(x => x.Title).IsRequired();   // RequiredConstraint
        });

        modelBuilder.Entity<Patron>(p => {
            p.HasKey(x => x.Email);          // unique property → natural key
            // Collection navs: backing field access mode
            p.Metadata.FindNavigation(nameof(Patron.Loans))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
            // ...
        });

        // Entities without a unique/natural key → auto-generated shadow key "Id"
        modelBuilder.Entity<Loan>(l => {
            l.Property<int>("Id");
            l.HasKey("Id");
        });
    }
}
```

#### Decisions to make

| Decision | Options | Recommendation |
|----------|---------|----------------|
| Natural key selection | Unique constraint → key vs. shadow key always | Use `unique` constraint as natural key; entities without `unique` get shadow key `Id` |
| Pluralization for `DbSet` name | `Book` → `Books`, `PatronPatron` → `Patrons` | Simple `+ "s"` suffix; override via DSL annotation later |
| Owned navigation support | Emit `.OwnsOne()` / `.OwnsMany()` | Defer until `owned` keyword is used in DSL |
| Mapping attributes vs. fluent API | Data annotations vs. `OnModelCreating` | **Fluent API** — keeps entities clean; matches experiment pattern |

#### Files to create

| File | Purpose |
|------|---------|
| `Poly/DomainModeling/Lowering/DbContextExporter.cs` | Produces `TypeDefinitionNode` for `LibraryDbContext` |
| `Poly.Tests/DomainModeling/Lowering/DbContextExporterTests.cs` | Tests for each entity pattern |
| `src/Poly.DslCompiler/DbContextBackend.cs` | Wire into DslCompiler (optional output mode) |

#### Test coverage

| Scenario | Test |
|----------|------|
| Entity with `unique` property → natural key | `DbContext_EntityWithUnique_UsesNaturalKey` |
| Entity without `unique` → shadow key Id | `DbContext_EntityWithoutUnique_UsesShadowKey` |
| `RequiredConstraint` on property → `.IsRequired()` | `DbContext_RequiredProperty_IsRequired` |
| `LengthConstraint` on property → `.HasMaxLength()` | `DbContext_LengthConstraint_HasMaxLength` |
| Collection navigation → field access mode | `DbContext_CollectionNav_UsesFieldAccess` |
| Singular navigation → no property config | `DbContext_SingularNav_NoScalarConfig` |
| Enum property → no special mapping | `DbContext_EnumProperty_NoMapping` |
| Stage enum → not mapped as DbSet | `DbContext_StageEnum_NotMapped` |
| Multi-entity domain → one DbSet per entity | `DbContext_MultiEntity_AllDbsetsPresent` |

---

### Phase B — Minimal API generation

**Goal:** Generate `Program.cs` with:
1. `WebApplication.CreateBuilder` + JSON/EF config
2. CRUD endpoints for every entity
3. Action endpoints for every entity action
4. Seed data support (optional)

#### Output shape

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseInMemoryDatabase("Library"));
var app = builder.Build();

// ── Book CRUD ──
app.MapGet("/api/books", async (LibraryDbContext db) => await db.Books.ToListAsync());
app.MapGet("/api/books/{isbn}", async (string isbn, LibraryDbContext db) => ...);
app.MapPost("/api/books", async (BookDto dto, LibraryDbContext db) => ...);

// ── Patron CRUD ──
app.MapGet("/api/patrons", ...);
app.MapGet("/api/patrons/{email}", ...);
app.MapPost("/api/patrons", async (PatronDto dto, LibraryDbContext db) => {
    var result = Patron.Create(...);
    if (!result.IsSuccess) return Results.Conflict(new { error = result.ErrorMessage });
    db.Patrons.Add(result.Value);
    await db.SaveChangesAsync();
    return Results.Created($"/api/patrons/{result.Value.Email}", result.Value);
});

// ── Patron Actions ──
app.MapPost("/api/patrons/{email}/checkout", async (string email, CheckOutDto dto, LibraryDbContext db) => {
    var patron = await db.Patrons.Include(p => p.Loans)...
    var result = patron.CheckOut(book);
    ...
    return result switch {
        { IsSuccess: true, Value: var loan } => Results.Ok(loan),
        { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),
    };
});

app.Run();

// ── DTOs ──
record BookDto(string Author, Genre Genre, string ISBN, long Pages, string Title);
record PatronDto(string Name, string Email, long MaxItems, long CurrentBorrowCount, long? OutstandingFines);
record CheckOutDto(string ISBN);
```

#### Anatomy of one endpoint

```
GET    /api/{entities}                     → list (no DTO needed)
GET    /api/{entities}/{naturalKey}         → single
POST   /api/{entities}                     → create (with constraint validation)
POST   /api/{entities}/{naturalKey}/{action} → action invocation

For entities without natural key:
GET    /api/{entities}/{id}                 → single by shadow key
DELETE /api/{entities}/{id}                 → delete (optional Phase B.2)
```

#### Parameter naming for action endpoints

Action parameters become JSON body fields. The DTO name follows the action:

```
CheckOut: action (book: Book)   →   CheckOutDto { ISBN }
Suspend: action                 →   no DTO needed
```

#### Files to create

| File | Purpose |
|------|---------|
| `Poly/DomainModeling/Lowering/MinimalApiExporter.cs` | Produces `TypeDefinitionNode` + statements for `Program.cs` |
| `Poly.Tests/DomainModeling/Lowering/MinimalApiExporterTests.cs` | Tests for endpoint generation |
| `src/Poly.DslCompiler/ApiBackend.cs` | Wire into DslCompiler |

#### Test coverage

| Scenario | Test |
|----------|------|
| Entity with natural key → GET by key endpoint | `Api_EntityWithKey_GetByKeyEndpoint` |
| Entity without key → GET by shadow id | `Api_EntityWithoutKey_GetByIdEndpoint` |
| Entity with no `unique` → POST with all non-default params | `Api_EntityPost_CreatesFromDto` |
| Entity with actions → action endpoints | `Api_EntityWithActions_ActionEndpoints` |
| Action with parameters → DTO generated | `Api_ActionWithParams_DtoGenerated` |
| Constraint validation in POST → 409 on failure | `Api_PostWithInvalidData_ReturnsConflict` |
| `DomainResult<T>` switch → HTTP 200/409 | `Api_ActionResult_MapsToHttpStatus` |
| `DomainResult` void action → HTTP 200/409 | `Api_VoidAction_MapsToHttpStatus` |
| Stage guard → 409 with stage message | `Api_StageGuard_ReturnsConflict` |
| Policy guard → 409 with policy message | `Api_PolicyGuard_ReturnsConflict` |

---

### Phase C — DslCompiler integration

**Goal:** `dotnet poly compile --api --db` produces `_all.cs` + `LibraryDbContext.cs` + `Program.cs`

#### Options surface

```bash
dotnet run --project src/Poly.DslCompiler \
  domain.poly output-dir/ \
  --mode entities          # default: entity types only (current behavior)
  --mode all               # entities + DbContext + API
  --mode db                # entities + DbContext only
  --mode api               # entities + API only
  --storage ef-inmemory    # default: EF Core InMemory
  --storage ef-sqlite      # future: EF Core SQLite
```

#### DslCompiler integration

```csharp
// DslCompiler.cs — new methods
public CompileResult CompileWithApi(string polyText, ApiOptions options)
{
    // 1. Parse + evolve (same as Compile)
    // 2. Generate entity types (same as Compile)
    // 3. Generate DbContext
    // 4. Generate Minimal API
    // 5. Return all files
}
```

---

### Phase D — Configurable storage backend

**Goal:** Support SQLite (and later SQL Server, PostgreSQL) instead of just InMemory.

#### Changes needed

```
--storage ef-sqlite
```

Adds `Microsoft.EntityFrameworkCore.Sqlite` package reference and changes
`UseInMemoryDatabase` to `UseSqlite("Data Source=library.db")`.

For production-grade backends, the DbContext mapping may need additional
configuration (column types, indexes, cascade behaviors). These can be
annotations on the domain model or a separate configuration DSL.

**Phase D is explicitly deferred** until a real consumer needs it. InMemory
is sufficient for the experiment + development loop.

---

## 4. Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Generated DbContext becomes too large to maintain | Hard to evolve | Keep `DbContextExporter` focused on entity→table mapping only; defer advanced EF features |
| Pluralization logic for DbSet names | Wrong names | Use simple `+ "s"` failover; document that manual override is expected for production use |
| Natural key vs. shadow key ambiguity | Wrong primary key | Rule: `unique` constraint → natural key; no `unique` → shadow key. Document explicitly. |
| `Include()` for navigations in GET endpoints | Lazy loading = N+1 | Emit `.Include()` for all navigations on list/detail endpoints |
| Action parameter DTOs conflict with entity DTOs | Duplicate types | Namespace or prefix: `CheckOutDto` vs `BookDto` — use action name + "Dto" suffix |
| `dotnet poly` CLI doesn't exist yet | No UX | Use `dotnet run --project src/Poly.DslCompiler` as today; CLI packaging is separate work |
| Generated code needs framework packages | NuGet restore issues | Generate `.csproj` alongside `.cs` files with correct package references |

---

## 5. Non-goals (explicitly deferred)

| Feature | Why deferred |
|---------|-------------|
| SQL/SQLite storage backend | No named consumer yet; InMemory covers dev loop |
| Authentication / Authorization | Not a domain modeling concern |
| OpenAPI / Swagger generation | Can be added later via `AddSwaggerGen()` in generated `Program.cs` |
| Pagination / Filtering / Sorting | CRUD MVP is list-all + get-by-key |
| PUT / PATCH / DELETE endpoints | Minimal API MVP is GET + POST + actions |
| CORS configuration | Add when a real frontend needs it |
| Health check / diagnostics | Out of scope for codegen |
| Migration support | DbContext + entities can use EF migrations manually |
| CLI tool packaging (`dotnet poly`) | Separate from codegen logic |

---

## 6. Implementation order

```text
Phase A  ─── DbContext generation      ← smallest, highest leverage
  │
  ▼
Phase B  ─── Minimal API generation    ← biggest user-facing value
  │
  ▼
Phase C  ─── DslCompiler integration   ← connects A + B into one command
  │
  ▼
Phase D  ─── Storage backends          ← pull when needed
```

### Recommended first slice (Phase A, thin)

1. Create `DbContextExporter.cs` with a single method: `Export(Domain, AnalysisResult)`
2. Produce `TypeDefinitionNode` for `LibraryDbContext` (one type, `OnModelCreating` body)
3. Wire into DslCompiler via `--mode db` flag
4. Verify output matches `demo/Poly.RestApi/Data/LibraryDbContext.cs`
5. **Test first:** Write the test suite before generating production code

### Recommended first slice (Phase B, thin)

1. Create `MinimalApiExporter.cs` — start with just CRUD (no action endpoints)
2. Produce `TypeDefinitionNode` for `Program.cs` (one type, top-level statements)
3. Wire into DslCompiler via `--mode api`
4. Verify output matches a stripped-down `Program.cs`
5. Expand to action endpoints + DTO generation

---

## 7. Success criteria

The plan is complete when:

1. `dotnet run --project src/Poly.DslCompiler -- domain.poly out/ --mode all`
   produces three files that compile and run without manual edits:
   - `_all.cs` (entity types + DomainResult)
   - `LibraryDbContext.cs` (EF DbContext)
   - `Program.cs` (Minimal API)
2. The generated API supports all CRUD + action endpoints from the experiment
3. `demo/demo.http` works against the generated output without changes
4. Constraint validation, stage guards, policy guards all produce HTTP 409 responses
5. Test suite adds at least 30 new tests across the two new exporters
6. Full suite remains green (existing 1478 tests + new tests)

---

## Appendix A: File inventory for new code

### New files in Poly/

| File | Purpose | Lines (est.) |
|------|---------|-------------|
| `Poly/DomainModeling/Lowering/DbContextExporter.cs` | DbContext generation | ~400 |
| `Poly/DomainModeling/Lowering/MinimalApiExporter.cs` | Minimal API generation | ~600 |

### New files in Poly.Tests/

| File | Purpose | Lines (est.) |
|------|---------|-------------|
| `Poly.Tests/DomainModeling/Lowering/DbContextExporterTests.cs` | DbContext tests | ~200 |
| `Poly.Tests/DomainModeling/Lowering/MinimalApiExporterTests.cs` | API tests | ~300 |

### Modified files

| File | Change |
|------|--------|
| `src/Poly.DslCompiler/DslCompiler.cs` | Add `CompileWithApi()`, `--mode` flag |
| `src/Poly.DslCompiler/Program.cs` | Parse `--mode` argument |

### Files that DON'T change

| File | Reason |
|------|--------|
| `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` | Entity codegen is stable |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | No new effects needed |
| `Poly/Interpretation/CSharp/CSharpGenerator.cs` | Preamble + formatting is stable |
| `Poly/DomainModeling/Action.cs` | Action model is stable |

---

## Appendix B: Relationship to platform trust bar

The platform trust bar says: **We are our own first customer.** Generation funds
neurosymbolic platform work over time. This plan aligns by:

1. **Generating real, runnable applications** from the DSL — proving the domain
   model is complete enough to power a production stack
2. **Dogfooding** — the `demo/RestApi` experiment already proved the concept;
   this plan productionizes the manual work
3. **Delaying depth** — storage backends (Phase D) and authentication are deferred
   until a real consumer needs them
4. **Keeping entity codegen stable** — the new exporters consume the same
   `Domain` model as the existing pipeline; no changes to core lowering

Per AGENTS.md: "Customer product generation funds neurosymbolic work over time —
generation is the engine, not a side demo."
