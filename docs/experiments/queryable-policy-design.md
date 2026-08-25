# Query Surface Design — Queryable Properties + Policy Parameters

**Date:** 2026-07-22  
**Status:** Design — future feature, not yet in migration ladder  
**Related:** [`docs/plans/archive/infrastructure-pass/infrastructure-concern-analyzer-suite.md`](../plans/archive/infrastructure-pass/infrastructure-concern-analyzer-suite.md) §13 (archived)

---

A query surface exposes domain data to consumers through structured read endpoints. Three orthogonal decisions cover ~99% of cases, each adding zero new keywords to the language:

| Construct | What it exposes | How the generator uses it |
|-----------|---------------|--------------------------|
| `queryable` property facet | Single-property filter/sort | `?Title=...`, `?Price=10&Price=50`, sort clause |
| Policy with Swift-style dual-name params | Compound boolean expression | `?min=10&max=50` — parameter name is the external label |
| Policy as `require` guard | Same expression, runtime use | No API surface — just an action gate |

## 1. `queryable` on properties

A facet on the property — no new keyword:

```poly
entity Book {
  Title: Text queryable          // equality / LIKE filter, sortable
  Price: Decimal queryable       // range filter, sortable
  PublishedAt: Date queryable    // date range, sortable
  CoverImage: Binary             // NOT queryable — no filter, sort, or projection
}
```

**Behavior per type:**

| Type | Filter shape | Sort | Project |
|------|-------------|------|---------|
| `Text queryable` | Equality / `LIKE` | ✅ | ✅ |
| `Decimal` / `Number queryable` | Min/max range | ✅ | ✅ |
| `Date` / `DateTime queryable` | Min/max range | ✅ | ✅ |
| `Boolean queryable` | Equality toggle | ❌ | ✅ |
| Enum `queryable` | Equality / `IN` | ❌ | ✅ |
| `Binary` | ❌ (diagnostic: not a queryable type) | ❌ | ❌ |

**Named vs unnamed:**

```poly
Price: Decimal queryable               // generic: ?Price=10&Price=50
Price: Decimal queryable("by_price")   // named:   GET /api/books/by_price?Price=...
```

The unnamed form adds the property to a generic query collection on the list endpoint. The named form creates a dedicated route segment. Both resolve to the same storage lowering (range-aware `WHERE` clause).

**No policy body needed for single-property cases.** The generator reads the property type, constraints (`required`, `range`, `pattern`), and chooses the right operator. A `Decimal` with `range(0, 10000)` gets clamped query parameters. A `Text` with `pattern("^[A-Z]+$")` gets regex validation on the filter value.

## 2. Policy parameters and Swift-style dual naming

Policies gain an optional parameter list, exactly mirroring action syntax. The Swift-style `external internal: Type` convention gives the public API name without facets or annotations:

```poly
entity Book {
  Price: Decimal

  // Policy with params — body uses internal names; API uses external names
  InStock(include_borrowed borrowed: Bool): bool
    { QuantityOnHand > 0 or borrowed }

  // Zero-param policy — no API query parameter, still usable as runtime guard
  IsActive: bool
    { Status != "Archived" }
}
```

**Parser change:**

```
parameter  := identifier (identifier)? ':' type
              │          │
              │          └ internal body name (used in expression body)
              │
              └ external label (API query param, JSON field, DTO property)
```

When the second identifier is absent, `ExternalName == InternalName` — same as today. When present, they diverge. No new keywords, no new token types.

**Generator consumption:**

| Consumer | Reads | Produces |
|----------|-------|----------|
| `RestApiSurfacePass` | `Parameter.ExternalName` on policy params | `QueryableEndpoint` metadata with resolved parameter names |
| `MinimalApiGenerator` | `QueryableEndpoint` | `?include_borrowed=false` route + OpenAPI `name: include_borrowed` |
| `OpenApiEmitter` | `QueryableEndpoint` | Schema property name override |
| `DbContextGenerator` / Dapper repo gen | Policy expression tree + `StoragePass` field map | Pre-compiled `Expression<Func<T,bool>>` or raw `WHERE` clause |
| EF compiled query gen | Policy body as `DomainExpression` | `static Func<DbContext, TParam1, TParam2, IQueryable<T>>` |

## 3. Parameterized gate invocations

Because `policy = action returning bool`, any site that references a policy may pass arguments — including `require` gates on actions and `entry`/`exit` on stages:

```poly
entity Order {
  Total: Decimal

  // Policy definition
  IsOver(minimum: Decimal): bool { Total >= minimum }

  // Action guard — passes argument to policy
  Discount5: action when Submitted require IsOver(100) { ... }
  Discount10: action when Submitted require IsOver(500) { ... }

  // Stage entry guard — same policy, different argument
  Checkout: entry require IsOver(0)  // can't start with negative total
}
```

The expression tree already has `Invoke` nodes. The analysis hub already resolves qualified names. No engine changes — the `require` parser already accepts policy names; adding optional parenthesized arguments uses the existing argument-list grammar from action invocations.

## 4. Blanket queryability — no opt-in keyword on policies

All policies are queryable by default. The fail-closed mechanism replaces the keyword:

- **A policy like `InStock(...) { QuantityOnHand > 0 or borrowed }`** — the expression tree contains only member access and parameter references. The storage lowerer produces a perfect `WHERE` clause. No ceremony needed.

- **A policy like `IsPreferred { CurrentUser.IsInRole("Admin") }`** — the expression tree references `CurrentUser`, a runtime context symbol the storage lowerer doesn't recognize. It emits a diagnostic: *"Policy 'IsPreferred' references runtime symbol 'CurrentUser' which cannot be lowered to a storage filter."* Fail closed — no wrong results, no silent omission.

Parameters × policy → endpoint. No parameters → internal filter (usable as `HasQueryFilter` or repo helper, but not an API query parameter). The presence of parameters *is* the declaration of intent to expose.

## 5. Per-medium lowering

Each medium pack owns a lowerer that projects the policy expression tree into its native filter dialect:

| Target | Lowering | Example output |
|--------|----------|---------------|
| **EF Core** | `DomainExpression` → `Expression<Func<T,bool>>` | `b => b.Price >= min && b.Price <= max` |
| **EF compiled query** | Pre-compiled `static` query | `static CompileQuery(...)` |
| **Dapper** | `Expression` → raw SQL `WHERE` clause | `WHERE Price >= @min AND Price <= @max` |
| **MongoDB** | `Expression` → `FilterDefinition<T>` | `Builders<Book>.Filter.Gte(x => x.Price, min) & ...` |
| **DynamoDB** | `Expression` → condition expression or **fail closed** | Only lowered if all filter properties are key/GSI attributes |
| **In-memory / test** | None needed — VM evaluates directly | `Interpreter.Evaluate()` with entity instance |

The lowerer is an **artifact consumer** — it reads `StorageMappingMetadata` (field name map), `BehaviorMetadata` (policy expression tree + parameter types), and produces target-specific filter IR. No new analysis passes. Exactly the ADR §1d pattern.

## 6. What this means for the REST API surface

The `RestApiSurfacePass` gains a new metadata slot:

```csharp
public sealed record RestApiMetadata(
    IReadOnlyList<RestEndpoint> Endpoints,
    IReadOnlyList<DtoShape> Dtos,
    IReadOnlyList<SeedHint> Seeds,
    IReadOnlyList<QueryableEndpoint> QueryEndpoints   // NEW
) : IAnalysisMetadata;

public sealed record QueryableEndpoint(
    string Route,                             // e.g. "/api/books" or "/api/books/by_price"
    string PolicyName,                        // e.g. "InPriceRange"
    IReadOnlyList<QueryableParameter> Params, // resolved external names + types
    bool IsSingleProperty                     // from property queryable facet vs policy body
);
```

**Generator consumption — MinimalApi:**

```csharp
// Policy: InPriceRange(min_price minPrice: Decimal, max_price maxPrice: Decimal): bool
// → GET /api/books/in_price_range?min_price=10&max_price=50
if (queryEndpoint.IsSingleProperty) {
    // property facet: simple equality or range with inferred operators
} else {
    // policy body: use the pre-compiled Expression<Func<T,bool>>
}
app.MapGet(queryEndpoint.Route, async ([AsParameters] QueryParams q, LibraryDbContext db) =>
    await db.Books.Where(CompiledQueries.InPriceRange(q.min_price, q.max_price)).ToListAsync()
);
```

**Generator consumption — Dapper:**

```csharp
// Same metadata produces:
public interface IBookRepository {
    Task<IReadOnlyList<Book>> GetByInPriceRangeAsync(
        decimal? minPrice, decimal? maxPrice, IDbConnection db);
}
```

## 7. Relationship to the pass suite architecture

| Seam | How it's exercised |
|------|-------------------|
| `BehaviorPass` | Already surfaces policy name → `DomainExpression` tree + parameter declarations |
| `StoragePass` | Already maps property names → field/column names for WHERE clause generation |
| `RestApiSurfacePass` | Gains `QueryableEndpoint[]` metadata — reads policy params, resolves external names |
| `MinimalApiGenerator` | Consumes `QueryableEndpoint` to emit query parameter binding + `Where(...)` or compiled query |
| `DbContextGenerator` | Consumes policy expression tree + storage field map for pre-compiled queries |
| Pack lowerers | Per-medium consumers (EF, Dapper, MongoDB) — no new passes, pure artifact consumers |

No new analysis passes. No new keywords. Three syntax changes (dual-name params, policy parameter list, `queryable` property facet) — all parser-level, all using existing mechanisms (parameter grammar, facet on property). The analysis hub absorbs the new metadata without structural changes. The generator adapters consume from the same `AnalysisResult` every other consumer reads.

## 8. Implementation status

This feature is **not in the current migration ladder**. It depends on:

- Parser support for policy parameter lists (similar to action parameters)
- Swift-style dual-name parameter syntax (`external internal: Type`)
- `queryable` facet registration on properties
- Policy expression tree lowering to storage filters (per-medium)

All of these are parser-level changes that can be implemented incrementally. The analysis hub and pass architecture are already prepared to absorb the new metadata without structural changes.
