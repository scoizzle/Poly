# Scoped unique constraints — design and migration

**Date:** 2026-11-06
**Repo evidence:** `/Users/scoizzle/.t3/worktrees/Poly/t3-4cc2823d` at `aa142413` (read-only; nothing in the tree was modified except this note).
**Companion:** `prior-art.md` in this folder (external semantics). This note is the repo-grounded design the prior art implies.
**Scope constraint:** current `.poly` + MCP v2 surface, no engine rewrite, no second uniqueness runtime.

---

## 0. Recommendation in one page

| Decision | Recommendation |
|---|---|
| Syntax | `Code: Text unique(TenantId)`. Bare `unique` keeps meaning **global**. One keyword, one optional parenthesised argument. |
| Meaning of `unique()` | Parse error. An empty scope has no honest semantics (global is already `unique`; per-instance is vacuous). |
| Scope operand | A **required, never-assigned, `Text` scalar property on the same entity**. Not a navigation, not a dotted path, not an entity-typed property. |
| Value requiredness | A scoped-unique value must be **required** (no `optional`). No silent "NULL is exempt" semantics. |
| Store seam | Extend the existing `EnsureUnique` call shape and the existing storage library. Add **`IScopedStoragePort : IStoragePort`** (additive) and a composite `(scopeProperty, scopeValue, value)` pending key. **No** new `ScopedUniqueIndex` runtime. |
| DB | EF **composite unique index** `HasIndex("<scopeColumn>", "<valueColumn>").IsUnique()` — the only real enforcement under concurrency. |
| Emission | JSON Schema / SHACL get `"x-poly-unique": { "scope": "<PropertyName>" }` (annotation only). No `owl:hasKey` in this slice. |
| Solver | `poly_check` proves only the **structural** rules (scope resolves, is required, immutable, ctor-supplied). It must say it **cannot prove the key** — Hint + coverage owner. |
| Migration | Purely additive. Global `unique` output, messages, and emission are byte-identical. Syntax arm lands **last** in a 5-commit PR so nothing half-enforced is authorable. |

The honest claim, in one sentence: **`unique(Scope)` means "at most one entity of this type has this value for each distinct `Scope` value", enforced by a composite unique index and pre-checked by the module; it does not tie `Scope` to any navigation, and it proves nothing about keys in `poly_check`.**

---

## 1. Syntax: `unique_within(parent)` vs `unique_to(parent)` vs `unique(parent)` vs `key(...)`

### Takeaway
Ship **`unique(scope)`** — the existing `unique` keyword with an optional parenthesised scope. It is the only option that adds **zero vocabulary**, keeps every existing `.poly` file parsing and printing identically, and round-trips through the one printer arm that already exists for `unique`.

### Cited findings

- `unique` is a reserved token and a bare constraint: `DslTokenKind.Unique`, `DslTokenReader` keyword map (`"unique" => DslTokenKind.Unique`), canonical text `"unique"`, parsed at `DomainResolver.ParseConstraint` (`case TokenKind.Unique: Advance(); return new UniqueConstraint();`). — `Poly/DomainModeling/Language/DomainResolver.cs:1494`, `Poly/DomainModeling/Language/DslGrammar.cs:85`, `Poly/DomainModeling/Language/DslTokenReader.cs:208`
- Every other constraint that takes an argument already uses `keyword(arg, ...)`: `range(0, 150)`, `length(3)`, `pattern("...")`, `default(...)`, `equals(...)` — `Poly/DomainModeling/Language/DomainResolver.cs:1498-1567`
- The printer is a single expression switch keyed on the constraint record; `UniqueConstraint => "unique"` — `Poly/DomainModeling/Language/DomainDslPrinter.cs:873-884`
- The resolver's `IsConstraint` gate is a flat token list (`Optional or Unique or Range ...`), which a second keyword would have to join — `Poly/DomainModeling/Language/DomainResolver.cs:1636-1641`
- All DSL keywords are single lowercase words (`optional`, `unique`, `range`, `length`, `pattern`, `default`, `equals`, `enum`, `owned`); there is no underscore-compound keyword anywhere in `DslGrammar.CanonicalText` — `Poly/DomainModeling/Language/DslGrammar.cs:40-104`
- LinkML models the same axis as a named, separate construct (`unique_keys` with `unique_key_slots`) rather than a suffix on a scalar, and it separates `identifier` (global) from `key` (container-scoped). — [LinkML constraints](https://linkml.io/linkml/schemas/constraints.html)

### Why the alternatives lose

| Candidate | Reads well? | Cost | Verdict |
|---|---|---|---|
| `unique(TenantId)` | "unique, within TenantId" — the parens read as a modifier argument exactly like `range(0,150)` | One parser `if (LParen)`, one printer arm, **no new token, no new reserved word** | **Recommended** |
| `unique_within(TenantId)` | Slightly clearer in isolation | New token kind + tokenizer keyword + canonical text + `IsConstraint` entry; a compound keyword breaks the single-word vocabulary; `unique_within()` still needs an empty-scope rule | Reject |
| `unique_to(TenantId)` | Reads like a relationship verb, not a key scope; suggests the row *belongs to* the parent, which this feature deliberately does **not** check | Same token cost as above plus a misleading name | Reject |
| `unique(Scope.Name)` (dotted) | Suggests cross-entity scope | Cross-entity read is not expressible in the lowered module (see §3), so it would be a parse-and-ignore hole | Reject |
| Entity-level `key(TenantId, Code)` | Correct home for **composite** uniqueness; also useful for route keys | New entity-level constraint position, new storage/index emission, new natural-key interaction, new migration surface | Defer; do not overload `unique` with it |

### What `unique_within()` with no scope means
Nothing. `unique` already means global; a zero-argument `unique_within` is either global (redundant) or "unique per instance" (vacuous). Recommendation: `unique()` is a **parse error** — `"unique(...) requires the name of a property on this entity to scope by."` — and `unique_within` never exists.

### Round-trip guarantee
`Code: Text unique(TenantId)` parses to `ScopedUniqueConstraint("TenantId")` and prints back with the **same spelling and casing** (the operand is a property name, printed verbatim, not a type name). Because the operand is an identifier, no escaping rule is needed. `PrintConstraint` is the only printer that must learn the arm; `DomainPresentation.Constraints` (the Markdown/HTML report path) and `ConstraintValidation.Describe` (diagnostics path) are the two other renderers that must stay in lockstep — `Poly/DomainModeling/Queries/DomainPresentation.cs:22-38`, `Poly/DomainModeling/Analysis/ConstraintValidation.cs:25-37`.

### Gaps
- No repo convention exists for a constraint that takes an *identifier* argument; all existing arguments are literals (numbers/strings/literals). The parser arm is trivial, but the printer's "identifier vs literal" distinction is new and should be pinned by a round-trip test on a scope whose name collides with a primitive-ish word (`Version: Text unique(TenantId)`, `TenantId` uppercase-first).
- Whether a future `key(...)` should be spelled on the entity or on the property was not decided here; see §7 "composite".

---

## 2. Scope rule: which operand is legal, and what the analyzer rejects

### Takeaway
The scope must be a **required, never-assigned, `Text` scalar property on the same entity**. The decisive repo fact is not taste: the module's create guard runs **before construction** and can only read constructor parameters, and the module has **no way to read a parent's key from a navigation**. A property-valued scope is the only shape that can be enforced on both the simulator and the printed/EF path today.

### Cited findings (the two hard constraints)

1. **The create guard runs before `new`, with only the signature in hand.**
   `BuildCreateConstraintChecks(entity, ..., ctorParams)` produces nodes inserted *before* `var created = new Entity(...)`; it resolves a value by looking up `signature.FirstOrDefault(p => p.Name == ToCamelCase(prop.Name))` and **silently `continue`s when the property is not a constructor parameter** (entry-assigned props are explicitly skipped). — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:917-934`, `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:501-545`
   Consequence: if the scope is not a ctor param, the guard cannot exist — and today it would disappear **silently**, which is the "known-wrong shape" the tenets ban.
2. **The lowered module cannot read a parent's key from a navigation.**
   The printed entity carries navigations as plain CLR properties (`props.Add(new PropertyDefinitionNode(pascalName, OptionalTypeReference(targetType), ...))`) with no id/key member; the primary key is a **shadow** EF property (`ShadowKeyName` / `HasKey(shadowKey)`), and the generator emits **no** `HasOne`/`WithMany`/`HasForeignKey` at all (EF convention supplies the FK). — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:446-497`, `src/Poly.DslCompiler/DbContextGenerator.cs:277-302`, verified by grep (`HasOne|WithMany|HasForeignKey` has no hits in generated/emitted code).
   Consequence: `unique(TenantNav)` could only be checked by a host that resolves `tenant` → key. The HTTP route does know the parent key, but an **action that assigns the value** has only `this.Tenant`, not a key, so enforcement would be create-only — unsound.

### The rule set (all fail closed, all statically decidable)

| # | Rule | Diagnostic | Why |
|---|---|---|---|
| R1 | Operand names a **property** on the same entity (not a nav, not `this`, not dotted) | `DMCS003` error | Only a scalar is readable in the guard |
| R2 | Operand type resolves to a **primitive `Text`** (v1) | error | The port is string-based (`Exists/Record/Release` take `string`); EF's comparison for non-string CLR types is `x.Prop.ToString() == value`, which does not translate reliably for enums and is provider-dependent for ints |
| R3 | Operand is **not `optional`** | error | A null scope would silently widen the key to a global bucket; SQL Server treats a NULL-containing composite key as duplicate-once and EF papers over it with a filter — three vendor behaviours, one wrong meaning (see §4) |
| R4 | Operand is **not entry-assigned and not assigned by any action/stage/subscription effect** | error | Scope mutation is a key mutation; there is no re-index path in v1 (see §3) |
| R5 | Operand **is not the constrained property itself** | error | `unique(Code)` on `Code` degenerates to global uniqueness with a confusing spelling |
| R6 | The **value** is not `optional` | error | "At most one non-null value per scope" silently exempts the rows users most need to name; prior art recommends at-most-one with **no** exemption |
| R7 | Operand is not also `unique` on the value (global + scoped on the same property) | **hint** | Global implies scoped; not unsound, just redundant |
| R8 | Operand has `default(...)` | **hint** | Constant scope ⇒ scoped uniqueness is effectively global; legal but surprising |
| R9 | Two scoped uniques with different scopes on one property | allowed, no diagnostic | A conjunction, sound. **Must be excluded from the duplicate-merge audit** (see below) |

**R4's mechanism:** `EntityStructureMetadata.AssignOnlyPropertyNames` already walks every action, stage action, entry/exit effect and subscription effect for `AssignEffect { Target: PropertyAccess }` — `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:163-195`. Rule R4 is a set-membership test on published metadata, not a new walk. R3/R6 use `RequiredPropertyNames` (`EntityStructureAnalyzer.ComputeRequiredPropertyNames`, `:136-143`).

**Implementation trap (must be in the PR):** `ConstraintQualityAnalyzer.ValidateJointConstraintSatisfiability` groups constraints **by CLR type** and merges each group, reporting "contradictory … unsatisfiable" when `Merge` returns null. `ConstraintMerge` has no arm for a new record, so two `ScopedUniqueConstraint`s on one property would produce a **false unsatisfiability error**. Either add a same-scope/different-scope arm or (recommended) add the type to the exclusion list next to `EqualityConstraint` — `Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs:91-108`, `Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs:13-26`.

**Where the code lives:** a new `Poly/DomainModeling/Ontology/Constraints/ScopedUniqueConstraint.cs`:

```csharp
public sealed record ScopedUniqueConstraint(string ScopeProperty) : Constraint;
```

A **sibling record, not a field on `UniqueConstraint`.** Every existing consumer tests `c is UniqueConstraint` and means *global*:
`EntityStructureAnalyzer` natural-key detection (`:44-51`), `StorageAnalyzer` column uniqueness and parent key (`:201-207`, `:409-411`), `EntityStructureAnalyzer.EmptyIsLegal` (`:197-215`), `DomainToCSharpExporter` `EnsureUnique` presence and body (`:1007-1009`, `:1034-1063`), `ModuleInstanceExecutor.SetProperty` and `UniqueCollision` (`:287`, `Coercion:120`), `EffectLoweringPass.IsUniqueProperty` (`:359-372`), `JsonSchemaExporter`/`ShaclExporter`, `DomainPresentation`, `ConstraintValidation`, `ConstraintCheckWriter.CoverageByConstraint`. A field on `UniqueConstraint` would silently make a scoped value the entity's **natural key** and the **HTTP route key** (`StorageAnalyzer` `keyName`, `MinimalApiGenerator` `store.KeyName`) — a real bug. With a sibling record, the default of every un-audited site is *correct* (scoped is not global), and the sites that must know are explicit.

### Gaps
- Not established whether `Text`-only is too tight for the first users. The repo's own natural keys can be `Int` (`RuntimeAnalysisCache.ClrTypeName` maps the unique property's type), so `unique(TenantId)` with `TenantId: Int` is a plausible request. The blocker is only the EF side (`.ToString()` translation); a follow-up can widen the port to `long`/enum and emit a typed comparison. Recommend obeying R2 in v1 rather than shipping an unproven translation.
- No repo measurement of how often domains already carry an explicit `tenantId`/`accountId` scalar. If most do not, the scalar-scope design has an adoption cost (authors must add the column), which the nav design would have avoided — at the price of unenforceability documented above.

---

## 3. Store: key shape, check points, free/delete, concurrency, and the port seam

### Takeaway
Extend the existing **storage-owned `unique`** path; do not build a second index. The composite key is `(entity, property, scopeProperty, scopeValue, value)`; check on **create** and on **assign of the value**; never on link/unlink; never on delete (row delete frees the key in the DB); **no reparent in v1** because the scope is immutable. The one new seam is `IScopedStoragePort`, additive to `IStoragePort`.

### Cited findings (what exists today)

- `unique` compiles to **one storage obligation**: a create guard (`db.Exists` → `DomainResult.Failure`) and a post-construct `db.Record`, plus an implied `db` parameter threaded to callers — `Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs:28-80`
- The assign path is a **rewrite of a fixed shape**: `AssignObligations` injects `db` into any method containing `EnsureUnique` (`:82-101`), then `AfterApply` pattern-matches `Assignment(dest, Invoke(this.EnsureUnique, [Constant property, value]))` followed by an `IsSuccess` guard and emits `Exists` / `Release(previous)` / `Record` (`:103-115`, `:248-322`)
- The port is deliberately tiny and read-only: `bool Exists(entity, property, value)`, `void Record(...)`, `void Release(...)`, "no other writes and no `SaveChanges`" — `Poly/DomainModeling/Runtime/IStoragePort.cs:4-19`
- The session port is a journaled `HashSet` keyed by `entity␟property␟value`, with `Mark`/`RollbackTo` for transaction rollback — `Poly/DomainModeling/Runtime/ModuleStoragePort.cs:8-41`
- The generated EF context is the host half of the same port: `Exists` iterates each entity's unique columns and builds `Any(e => e.Col == value)`; `Record`/`Release` use the same 3-part pending key (literal `"\u001f"`) — `src/Poly.DslCompiler/DbContextGenerator.cs:155-238`
- The runtime host also enforces `unique` for a direct write (`SetProperty`) and for the "failed create adopted by create-in" path (`UniqueCollision`), including `Release` of the old value — `Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs:276-304`, `Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Coercion.cs:114-129`
- The *only* release path that exists is the assign-replaces-value `Release(previous)`; there is **no Delete** operation in the executor (`Create`/`Invoke`/`Link`/`Unlink`/`SetProperty` are the whole host surface) — `Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs`
- `Link`/`Unlink` mutate only the relationship field and the subscriber registry; they touch no storage key — `Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs:22-86`

### Design

**Key.** Composite, ordered, and namespaced so it can never alias a 3-part global key:

```
scoped  = "\u001e" + entity + "␟" + property + "␟" + scopeProp + "␟" + scopeValue + "␟" + value
global  =             entity + "␟" + property + "␟" + value
```

The `\u001e` (record separator) prefix is one byte and makes the two key spaces provably disjoint; the current separator is `\u001f` (`ModuleStoragePort.cs:41`, `DbContextGenerator.PendingKey:233-238`). Reuse the same `␟` separator inside.

**Port.** New interface in `Poly/DomainModeling/Runtime/IScopedStoragePort.cs`:

```csharp
public interface IScopedStoragePort : IStoragePort {
    bool Exists(string entity, string property, string scopeProperty, string scopeValue, string value);
    void Record(string entity, string property, string scopeProperty, string scopeValue, string value);
    void Release(string entity, string property, string scopeProperty, string scopeValue, string value);
}
```

- **Why a separate interface, not default interface methods on `IStoragePort`:** a DIM would either (a) fall back to the unscoped method — silently dropping the scope, the exact known-wrong shape the tenets ban — or (b) throw at runtime. A distinct type makes the requirement visible in the **emitted method signature** (`IScopedStoragePort db`), so a host that does not implement it fails at compile time. It is additive: existing `IStoragePort` implementers keep compiling.
- **Required plumbing (2 small spots):** `ModuleInstanceExecutor.IsStoragePort` matches `IStoragePort` (`:705-707`) and `CatalogIndex` does the same (`:78`); both need `|| typeof(IScopedStoragePort).IsAssignableFrom(...)` so the parameter is filled instead of treated as a domain argument. `ModuleStoragePort` implements the new interface (same journal, same rollback). `DbContextGenerator` adds `IScopedStoragePort` to the interfaces list and three methods (`:100-108`).
- **The executor must supply one object** (`_port`) that satisfies both; `MinimalApiGenerator` already threads the context object (it matches on `IStoragePort` at `:296-305`), so the route still passes one argument.

**Check points.**

| Event | Scoped check? | Why |
|---|---|---|
| Create (own factory, including `create in`) | **Yes** — guard before `new`, read the **scope ctor param** and the value ctor param; `Record` after construction | Mirrors the existing `ConstraintAnchor.Create` placement (`ConstraintContribution` before/after statements) |
| Assign of the value | **Yes** — the existing `EnsureUnique`-then-assign shape, rewritten to read `this.<Scope>` (stable, because R4 forbids assigning it) and `Release(this.<Value>)` first | Same rewrite machinery as today |
| Assign of the scope | **No such thing** — rejected at analysis (R4) | No re-index path in v1 |
| Link / Unlink of a parent collection | **No** | Links never touch the value or the scope; a stale scope relative to a nav is the documented cost of a value-keyed scope (§7) |
| Reparent (link to a new parent) | **No** | Same reason; nothing in the model ties the scope to the nav |
| Delete | **No host hook** — the DB deletes the row and the index entry with it | There is no Delete on the executor surface; adding one is a separate feature |
| Failed operation rollback | **Yes, automatic** — `ModuleStoragePort.RollbackTo(mark)` already journals every Record/Release | The executor's `Transact` already marks/rolls back (`ModuleInstanceExecutor.cs:315-348`) |

**Where the guard gets built.** The scoped compiler is a new `IConstraintCompiler` in the same storage library (`ScopedUniqueConstraintCompiler`), registered next to `new UniqueConstraintCompiler(Id)` in `PersistenceEmitLibrary` (`Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs:25`). It must:

1. `request.RequireStorage()` and fail closed with the existing message shape when the domain has no storage mapping (`ConstraintCompiler.cs:117-121`).
2. Resolve **both** the value parameter and the **scope** parameter from the `Create` signature. Today `ConstraintCompileRequest.Request(…)` builds `parameters` keyed by **method name** and only for the *constrained property's* camelCase name — `Poly/DomainModeling/Compile/ConstraintCompilation.cs:86-100`. Add an accessor, e.g. `ParameterFor(string methodName, string propertyName)`, and have the scoped compiler **throw** when either is missing:
   `"Scoped unique 'Code' needs both 'code' and 'tenantId' as constructor parameters of 'Line'."`
   Throwing here is the fix for constraint #1 in §2: a silent `continue` becomes a positioned compile failure.
3. Emit the guard as `db.Exists(EntityName, "Code", "TenantId", tenantId, code)` with the **same message text** the executor and the EF path use (one helper on the constraint record — `ScopedUniqueConstraint.ViolationMessage(entity, property, scope)` — used by all four sites).
4. `AssignObligations` reuses the existing `ContainsAssignOf` shape and injects `IScopedStoragePort db`; `AfterApply` reuses the `AssignWalk` pattern but reads `this.<Scope>` for the scope string instead of a parameter, and must **not** change the `EnsureUnique` call shape. If the rewrite is missed, the residual `EnsureUnique` still refuses ("A unique assign needs the storage port") — fail-closed, just a worse message. Cover it with a test.
5. `EffectLoweringPass.IsUniqueProperty` must return true for scoped columns, or the `EnsureUnique` call is never emitted. It already reads `StorageColumn.IsUnique` (`:359-372`), so setting the column flag is enough.
6. `EntityStructureAnalyzer` must **not** pick a scoped property as the natural key (`HasNaturalKey`/`KeyPropertyName`/`KeyClrType`, `:43-51`) — automatic with a sibling record, but pin it with a test because it also determines the HTTP route key.
7. `EmptyIsLegal` should treat a scoped unique value as non-empty-legal (same as global unique) so assign-only optional ctor logic stays consistent — `:197-215`.
8. `ModuleInstanceExecutor.SetProperty` and `UniqueCollision` get the scoped arm: read the scope from the bag, `Exists`, `Release` the old value in the **same** scope (scope cannot change), `Record` the new. Message identical.
9. `ConstraintCheckWriter.CoverageByConstraint` needs a row for the new record type (the test fails otherwise): `[typeof(ScopedUniqueConstraint)] = new([], "storage port: scoped create guard in ScopedUniqueConstraintCompiler, assign via AssignObligations + AfterApply")` — `Poly/DomainModeling/Lowering/ConstraintCheckWriter.cs:255-268`.

**Concurrency (be explicit in the PR).** The port guard is a **pre-check**, not a lock: two processes can both pass `Exists` and one loses at the database unique index, which surfaces as a provider exception at `SaveChanges`, not as a `DomainResult.Failure`. That is already true of global `unique`; scoped adds no new hole. Say it in the guide's shipped-boundaries section next to the existing `unique` bullet (`Poly.Mcp/Docs/poly-dsl-guide.md:1016-1027`).

### Gaps
- No repo precedent for a second storage interface; the alternative (overloads on `IStoragePort` with default throw) is smaller but pushes the failure to runtime. Chosen compile-time failure on purpose; worth a second opinion at plan lock.
- The executor `_port` is `readonly ModuleStoragePort`; exposing it as `IScopedStoragePort` is a one-line type widening, but `ModuleInstanceExecutor.Storage` is public and typed `IStoragePort` — decide whether to add `ScopedStorage` or change the property type (breaking for consumers). Recommend adding a second property and leaving `Storage` alone.

---

## 4. Emission: what is honest to emit

### Takeaway
Emit the composite index (real enforcement), the port guard (real pre-check), and a **contract annotation** in JSON Schema/SHACL. Do not claim enforcement from JSON Schema or SHACL Core, and do not emit `owl:hasKey` in this slice.

### Cited findings

- JSON Schema already refuses to pretend: `case UniqueConstraint: schema["x-poly-unique"] = true;` with the comment "JSON Schema validates one document; uniqueness across documents is a store fact." — `Poly/DomainModeling/Queries/JsonSchemaExporter.cs:207-210`
- SHACL carries the same extension and declares `"x-poly-unique"] = "urn:poly:vocab/unique"` in its context — `Poly/DomainModeling/Queries/ShaclExporter.cs:47`, `:236-238`
- EF composite index: `HasIndex(p => new { p.FirstName, p.LastName })`; `.IsUnique()`; index naming convention `IX_<type>_<prop>_<prop>`; "Attempting to insert more than one entity with the same values for the index's column set will cause an exception" — [EF Core indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- EF SQL Server adds `'IS NOT NULL'` filters for **nullable columns in a unique index**, overridable with `HasFilter(null)` — [EF Core indexes, "Index filter"](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- SQL Server's raw unique index treats a composite key containing NULL as duplicate-once: "you cannot create a unique index on multiple columns if the combination of columns contains NULL in more than one row" — [SQL Server unique indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-unique-indexes). This is the concrete reason R3/R6 require non-null scope **and** value: it removes a three-vendor disagreement entirely.
- OWL `HasKey(CE (OPE…) (DPE…))` is identity entailment ("same key values ⇒ same individual"), not a validation rule — [OWL 2 Syntax §9.5](https://www.w3.org/TR/owl2-syntax/#Keys), [OWL 2 New Features F9](https://www.w3.org/TR/owl2-new-features/)
- SHACL Core has no uniqueness component at all (existence and cardinality are separate; uniqueness is not among them) — [SHACL](https://www.w3.org/TR/shacl/)

### Recommended emission

| Artifact | Global `unique` (unchanged) | Scoped `unique(TenantId)` |
|---|---|---|
| C# entity/module | `EnsureUnique` residual + port guard | same call shape; guard calls the scoped port overload |
| EF `OnModelCreating` | `b.HasIndex(x => x.Code).IsUnique()` | `b.HasIndex("TenantId", "Code").IsUnique()` — **string names**, because the scope is a mapped CLR property but the *value/scope pair* must be addressed positionally and EF's lambda form would require a synthetic property expression; string form is also required if a future nav-scope uses a shadow FK |
| EF `Exists` | per unique column: `Any(e => e.Code == value)` | also `e.TenantId == scopeValue` in the same predicate |
| JSON Schema | `"x-poly-unique": true` | `"x-poly-unique": { "scope": "TenantId" }` |
| SHACL | `"x-poly-unique": true` | `"x-poly-unique": { "scope": "TenantId" }` |
| Markdown/HTML report | `unique` | `unique(TenantId)` via `DomainPresentation` |
| `poly_check` | (nothing today) | `Hint` + coverage owner (see §5) |

**Do not emit** `owl:hasKey` in this slice. It would be *faithful* for a Text scope (`HasKey(:Line (:tenantId :code))` says two `:Line` individuals agreeing on both are the same individual — an entailment, not a rejection), and the SHACL exporter is a shapes document, not a TBox. Emitting it would (a) change ontology exports for a feature whose semantics are rejection, and (b) invite a reasoner to *merge* duplicates. Note it as a possible future TBox export with the mismatch called out.

**Do not emit** a partial/filtered index in v1. It is the right lowering for "unique among active rows" but that is a different feature (conditional uniqueness), and the prior art is clear that the predicate belongs in the model, not the index.

### Gaps
- No source or repo evidence for how consumers of `x-poly-unique: true` are supposed to read it; changing the value from `true` to an object is a **shape change** for scoped properties only. Grep shows only the exporter writes it (no reader in-repo), so it is safe, but external consumers of the JSON Schema are unknown.
- The physical index column order `(scope, value)` is chosen so the index also serves "list by scope" prefix queries; not measured (see §6 perf).

---

## 5. Solver: what `poly_check` can and cannot prove

### Takeaway
`poly_check` can prove the **scope rules** (R1–R6) and nothing about the key. It must report a `Hint` naming the enforcer, and it must not let "no errors" be read as "the key is proven". The existing coverage table already has the vocabulary for exactly this (`Owner` string, `marker:` prefix).

### Cited findings

- `poly_check` computes `verdict = errorCount > 0 ? "violated" : "proven"` and only counts errors/warnings, excluding Hints from the verdict — `Poly.Mcp/Surface/PolySurfaceTools.cs:222-258`
- `ConstraintCheckWriter.CoverageByConstraint` is exhaustive by constraint **type**, and a record with neither `Kinds` nor `Owner` fails `ConstraintWriterCoverageTests`; `unique` is `Kinds: []` + `Owner: "storage port: …"` — `Poly/DomainModeling/Lowering/ConstraintCheckWriter.cs:247-268`
- `ConstraintValidation.IsSatisfiedBy(UniqueConstraint) => true` with the comment "cannot validate statically; requires instance set" — `Poly/DomainModeling/Analysis/ConstraintValidation.cs:19`
- The analysis pipeline has no instance set: `AnalysisResult` is per-node metadata; instances exist only in `ModuleInstanceExecutor` — `Poly/DomainModeling/Analysis/AnalysisResult.cs`, `Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs`

### Recommended behaviour

- Verdict: unchanged. Structural errors → `violated`; otherwise `proven` (which, as today, means "this model has no analysis errors", not "every row satisfies every constraint").
- Add a `Hint` on the scoped property, alongside the existing unprojected-storage hints in `ConstraintQualityAnalyzer.NoteUnprojectedStorageConstraints` (`:65-80`):
  `"Property 'Code' is unique within 'TenantId'. That is a fact over instances: it is enforced by the generated composite index and the storage port, not provable by poly_check."`
- Coverage owner string (machine-readable): `"storage port: scoped create guard in ScopedUniqueConstraintCompiler.Compile, assign via AssignObligations + AfterApply, index in DbContextGenerator"`.
- **Refusal to claim more:** if `poly_check` is ever asked to prove a key (future `select`-based proof), return `unknown` with the same message. Do not invent a "proven" verdict for a store fact.

**Can cardinality/ownership refine it?** Only in two narrow, statically decidable cases; neither is worth v1:
- If the scope property is also globally `unique`, the scoped key is implied by the global key (R7 hint).
- If the scope property has an `equals("x")` constraint, all instances share one scope, so scoped uniqueness **is** global uniqueness — a sound equivalence a future solver could report. It is also the only case where R8's "constant scope" is provable.
Ownership does **not** refine it: the aggregate knows `IsRoot`/`AggregateParentName`, but not keys; and the scope is a value, not the owner link.

### Gaps
- `poly_check`'s current "Proven: no errors" wording already overclaims slightly for global `unique`; this design does not fix that, it only avoids making it worse. A separate issue could introduce a `storeFacts` list in `poly_check` data.
- No test asserts that a Hint with a key message does not flip the verdict; add one.

---

## 6. Migration and tests

### Takeaway
Nothing existing changes shape. Land it as **one PR of five green commits**, with the DSL parser arm **last**, so the feature is never authorable before it is enforced. Test the matrix in §6.3, including the parity gate (simulate vs printed C#) that already exists for global `unique`.

### 6.1 Back-compat ledger

| Surface | Change | Proof |
|---|---|---|
| `.poly` text | Old files parse identically; `unique` prints identically | round-trip suite, `PolyDslRoundTripTests` |
| `UniqueConstraint` record | unchanged (no new field) | compiles; no test churn |
| `EntityStructureMetadata.HasNaturalKey` | unchanged for global; a scoped property is **not** the natural key even if it is the only unique-ish property | new test |
| HTTP routes | unchanged: scoped entities without a global unique keep the shadow `id` route | `MinimalApiGenerator` test |
| `DbContext` output for old domains | byte-identical (no composite index emitted without a scope) | golden/IR test |
| `IStoragePort` implementers | compile unchanged (new interface is additive) | compile |
| JSON Schema/SHACL for global | `x-poly-unique: true` unchanged | exporter tests |
| Messages | scoped adds "… with the same 'TenantId'."; global message untouched | parity test pins both |

**Existing data:** there is no migration engine in the repo. The composite index is created by the host's EF migration; if existing rows already violate it, index creation fails — which is the correct fail-closed outcome. The PR body must state this, and the guide must say "adding `unique(Scope)` to a populated table requires the data to satisfy it first."

### 6.2 Five commits (each leaves the suite green)

1. **Ontology + analysis.** `ScopedUniqueConstraint`, `ConstraintValidation` (`IsSatisfiedBy`/`Describe`), coverage-table row, merge-audit exclusion, R1–R6 diagnostics (`ConstraintSatisfiability` or a new `DMCS003`), R7/R8 hints. Tests build entities programmatically (no DSL arm yet, so nothing is authorable).
2. **Storage model + EF.** `StorageColumn.IsScopedUnique`/`ScopePropertyName` (init-only, default false/null), `StorageAnalyzer` sets them, `DbContextGenerator` emits the composite index and the scoped `Exists` arms. IR tests + a real SQLite integration test (§6.3, T5).
3. **Port + guard.** `IScopedStoragePort`, `ModuleStoragePort`, `ScopedUniqueConstraintCompiler` (create guard + assign obligations + `AfterApply`), `ConstraintCompileRequest.ParameterFor`, `IsStoragePort` matching, executor `SetProperty`/`UniqueCollision`, `EffectLoweringPath.IsUniqueProperty` via the storage column. Simulate tests + parity test.
4. **Export + report + MCP.** JSON Schema, SHACL, `DomainPresentation`, `PropertyDetail` constraint text (optional but recommended so `poly_inspect` shows `unique(TenantId)`), `poly_check` hint, guide rows (`poly-dsl-guide.md:305`, `:1169`, `:1195`, shipped-boundaries bullet).
5. **Syntax.** `ParseConstraint` `if (LParen)` arm, `PrintConstraint` arm, round-trip + `unique()` error tests, guide "shipped" note, one dogfood DSL example (e.g. `Line` with `Code: Text unique(OrderId)`).

### 6.3 Test matrix

| # | Case | Where | Assertion |
|---|---|---|---|
| T1 | Parse/print round trip | `Poly.Tests/DomainModeling/Parsing/ScopedUniqueParseTests.cs` | `unique(TenantId)` → record → identical text; bare `unique` unchanged; `unique()` error; `unique_within(x)` error |
| T2 | Every scope rule R1–R6 | `Poly.Tests/DomainModeling/Analysis/ScopedUniqueAnalysisTests.cs` | one test per rule, exact message; R7/R8 produce Hint not Error |
| T3 | Natural key not hijacked | same file | entity with only `Code: Text unique(TenantId)` has `HasNaturalKey == false`, route key `id` |
| T4 | EF IR | `Poly.Tests/DomainModeling/Lowering/ScopedUniqueDbContextTests.cs` | `HasIndex("TenantId","Code").IsUnique()` present; no index for global; old-domain IR unchanged |
| T5 | Real DB | `Poly.Tests/DomainModeling/Lowering/DslCompilerSqliteScopedUniqueTests.cs` (EF Sqlite is already a test dependency) | same code under two scopes saves; same code same scope throws on `SaveChanges`; NULL value twice saves (and is **not** allowed by R6 — so this test asserts the analyzer rejected `optional` earlier, and a programmatic bypass is not offered) |
| T6 | Simulate: create | `Poly.Tests/DomainModeling/Compile/ScopedUniqueSimulateTests.cs` | A/1 ok, B/1 ok, A/1 again fails, message names scope |
| T7 | Simulate: assign | same | assign to a taken value in same scope fails; to the same value is ok; to a taken value in another scope needs a different scope instance (R4 blocks moving) → assert the analyzer error instead |
| T8 | Simulate: nested create in one action | same | two `create in` with the same `(scope,value)` fails before any save ("recorded pending key" path) |
| T9 | Print/simulate parity | `Poly.Tests/DomainModeling/Compile/ScopedUniqueSimulatePrintAgreeTests.cs` using `ParityScenario` | same success/failure and **byte-identical message** on both sides |
| T10 | Rollback | `ScopedUniqueSimulateTests` | a failed action releases the pending key (a later create of the same value succeeds) |
| T11 | Composite key isolation | same | a global `unique` value and a scoped value with the same text do not collide (different key namespaces) |
| T12 | Case | same | `"ABC"` and `"abc"` are distinct in simulate (ordinal); document that SQL collation may disagree |
| T13 | Exporters | `Poly.Tests/DomainModeling/Queries/JsonSchemaExporterTests.cs`, SHACL test | `x-poly-unique` object with `scope` |
| T14 | Evolve/rejected evolve | mirror `UniqueSurvivesExportTests` / `UniqueSurvivesRejectedEvolveTests` | adding a scoped unique survives export; a rejected evolve leaves the previous domain intact |
| T15 | MCP surface | `Poly.Tests/Mcp/ScopedUniqueSurfaceTests.cs` | `poly_define` → `poly_check` Hint + proven → `poly_export json-schema` has the extension → `poly_simulate` shows the violation |
| T16 | Coverage table | existing `ConstraintWriterCoverageTests` | fails unless the new type has a row (no new test needed, just don't forget) |

### 6.4 Performance measurement

The guard cost is one dictionary lookup with a longer string; the DB cost is one more column in an index. Measure, don't guess:

1. **Simulate:** 10k `Create` calls with a global unique vs a scoped unique; expect < 5% difference (extra string concat + segment). Record the command and numbers in the PR body, not as a permanent benchmark.
2. **Query plan:** create the generated SQLite DB, run `EXPLAIN QUERY PLAN SELECT 1 FROM Lines WHERE TenantId = ? AND Code = ?` and assert the plan uses `IX_Lines_TenantId_Code` (EF default name per the docs above). Add it as a one-off in the PR, or a test that asserts the index exists via `PRAGMA index_list`.
3. **Index order:** `(scope, value)` also serves prefix queries by scope; note it. Do not measure write amplification on a laptop; state the trade (one extra column in a B-tree per row) and leave it.

### Gaps
- No repo benchmark harness entry exists for domain create throughput; `Poly.Benchmarks` is VM-oriented. If a permanent benchmark is wanted it needs a new benchmark class, which is out of this slice.
- T5 cannot prove concurrency (two connections racing) without a second connection and transactions; recommend a single-connection duplicate test plus a documented statement that the index is the concurrency authority.

---

## 7. Risks

| Risk | Severity | Mitigation / honest statement |
|---|---|---|
| **Scope set after create** | high | R4 rejects any effect that assigns the scope. This is provable from `AssignOnlyPropertyNames`; no re-index path is built. |
| **Scope drifts from a nav** (child unlinked from parent A, linked to B, `TenantId` still A) | high | This is the cost of a *value*-keyed scope. State it: "`unique(Scope)` is unique per **value**, not per linked parent; nothing keeps `Scope` in sync with a navigation." The `Link`/`Unlink` paths are unchanged (`ModuleInstanceExecutor.Links.cs:22-86`). A nav-keyed scope (`unique(Tenant)`) is the follow-up and needs a readable key on the entity in the lowered module (see the gap in §2). |
| **Ownership cascade** | medium | If the entity is an owned child, deleting the parent deletes the rows and their keys with them. If it is a non-owned reference, Poly has no delete operation, so nothing to do. There is no `ON DELETE SET NULL` equivalent, and R3 forbids a null scope. |
| **Many-to-many** | low | Collection scopes are rejected (R1), and "unique across every parent it is linked to" is not expressible; reject with the R1 message, don't pretend. |
| **Schema round-trip** | medium | Three renderers must learn the arm (`DomainDslPrinter`, `DomainPresentation`, `ConstraintValidation.Describe`) or the text silently becomes `?ScopedUniqueConstraint` (`DomainDslPrinter.cs:883`). Pin all three in T1/T13. |
| **False "contradictory constraints"** | medium | Two scoped uniques on one property would trip the group-merge audit (`ConstraintQualityAnalyzer.cs:91-108` + `ConstraintMerge.cs:24`). Exclude the type explicitly. |
| **Silent guard drop** | high | If the scope is not a ctor param, `BuildCreateConstraintChecks` `continue`s (`.Notify.cs:521-524`) and the compiler's fallback `new Parameter(...)` emits a dangling reference (`UniqueConstraintCompiler.cs:37`). The scoped compiler must **throw** on a missing parameter; add a test that an entry-assigned scope is rejected at analysis *and* that a programmatic bypass throws at compile. |
| **Wrong natural key / route key** | high | A scoped value must never become `KeyPropertyName`/`keyName`. Sibling record + T3. |
| **Case/collation divergence** | medium | Simulate is ordinal; SQL is collation-dependent (SQLite case-sensitive, SQL Server default `_CI_AI`). Document; do not normalize; a future `caseInsensitive` flag is a separate decision. |
| **NULL semantics across vendors** | resolved by design | R3+R6 require non-null scope and value. Without that, SQL Server's composite-index NULL rule (`create-unique-indexes`) and EF's `IS NOT NULL` filter would make the meaning deployment-dependent. |
| **Concurrency** | accepted | The index is the authority; the port is a pre-check. Already true for global `unique`. Say it in the guide. |
| **Existing data fails the new index** | operational | No migration engine; index creation fails on duplicates, which is correct. Say so in the PR body and guide. |
| **`Text`-only scope** | product | Intentional v1 narrowing; widening to int/enum needs the typed EF comparison and a port change. Track as a follow-up, not a silent TODO. |

---

## 8. Open questions worth a second opinion at plan lock

1. **Sibling record vs scope field on `UniqueConstraint`.** Sibling is safer for every existing `is UniqueConstraint` site (13+ sites listed in §2) but risks a *forgotten* arm (e.g. `SetProperty`) silently not enforcing. The test matrix is the mitigation. The alternative (field + audit every site) has a worse failure mode: a scoped value becomes a global key and a route key.
2. **`IScopedStoragePort` vs overloads with default throws.** Compile-time failure vs smaller public surface. Recommend the interface.
3. **`Text`-only scope in v1.** If the first user needs `TenantId: Int`, the slice either widens (port + EF comparison) or the user adds a `Text` mirror column — decide before writing issues.
4. **Whether `unique(Scope)` should also imply the value is required** (R6) while global `unique` does not. Recommend yes for scoped, with a follow-up issue to align global, and say the asymmetry in the guide so agents do not infer it.
5. **`PropertyDetail` in `poly_inspect`** currently carries only a constraint **count** (`Poly/DomainModeling/Queries/DomainQueries.cs:174`). Showing `unique(TenantId)` is a small win for agents but changes a query record; decide in the MCP-surface review, not in this PR.
