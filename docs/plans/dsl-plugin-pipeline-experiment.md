# Experiment: DomainModeling Plugin Seams + Provider Pack Libraries

**Status:** P0 locked; **P1 + P2 + P3 implemented**. **P4:** multi-DBMS packs with **SQLite as first shippable pack** (host `--dbms sqlite`); SqlServer remains available. P4.4 vendor sugar + P4.5 MCP deferred.  
**Date:** 2026-07-21 (**rev 7** — SQLite first shippable pack + host wiring)  
**Supersedes:** [`docs/experiments/domain-plugin-extension-platform.md`](../experiments/domain-plugin-extension-platform.md)  
**Pointer:** [`docs/plans/domain-plugin-extension-platform.md`](domain-plugin-extension-platform.md)  
**Related:** `src/Poly.DslCompiler/` (first host), `InfrastructureModel`, AGENTS §6, anti-pattern 003

---

## 0. Intent (product framing)

Poly domain models are **DBMS- and host-agnostic**. Real deployments are not: Oracle, SQL Server, PostgreSQL, MySQL/MariaDB, SQLite, and cloud variants each need:

| Concern | Examples |
|---------|----------|
| Default type maps | `Text` → `VARCHAR2` / `nvarchar` / `text` |
| Identifier rules | 30-char Oracle names, quoting, case |
| Column overrides | name + native type on a property |
| Table / schema | `table("ORDERS")`, schema packs |
| Persistence codegen | EF Core provider, raw DDL, other ORMs later |
| Optional vendor sugar | `ora(...)`, `pg(...)` as thin syntax over shared IR |

**Architecture stance:**

1. **Core plugin seams live in `Poly` / `DomainModeling`** — facet IR, parse/print hooks, type-map registry, storage convention hooks, authoring-context pack set (compiler-first).
2. **Provider / target packs are separate libraries over time** — e.g. `Poly.Packs.Sql`, `Poly.Packs.Oracle`, third-party packs. Core must not take a dependency on any DBMS package.
3. **Hosts compose packs** — `Poly.DslCompiler` (P1+), MCP session (P4+) register pack contributions; they do not reimplement seams.
4. **Oracle (and every major DBMS) is a consumer scenario, not core IR.** Prefer portable annotations (`column`, `table`); vendor packs supply defaults and optional sugar.
5. **This plan’s facet surface is `DomainType` + `Property` only.** Relationship / Action / Stage facets are out of scope here.

This is **not** “build MEF on day one.” It **is** “design DomainModeling so a third-party Oracle pack can exist without forking Poly.”

---

## 1. Why now

The prior experiment was parked for lack of a named consumer. That consumer exists in outline:

| Artifact | Role today | Gap |
|----------|------------|-----|
| `InfrastructureAnalyzer` / `StorageModel` | Shared storage facts for codegen | SQL Server–ish defaults in `DomainTypeMapping`; `StorageColumn.Name => Source.Name` (no override); `TableName => Name + "s"` |
| `DbContextGenerator` | EF shape | Emits `IsRequired` / `HasMaxLength` only — does **not** emit `HasColumnName` / `HasColumnType` even when `ColumnType` is set |
| `PolyDslParser` / `DomainDslPrinter` | Closed constraint set | No pack-registered annotations; MCP constructs parsers with no pack set |
| `DslCompiler` | Host CLI | Hard-coded generators and `CompileMode` |

Multi-DBMS makes closed switches a scaling problem: every new engine would otherwise touch 5+ core files.

---

## 2. Layering (ownership)

```text
┌─────────────────────────────────────────────────────────────────┐
│ Hosts (compose pack contributions)                               │
│   Poly.DslCompiler (P1+) · Poly.Mcp pack wiring (P4+)            │
└────────────────────────────┬────────────────────────────────────┘
                             │ registers annotation syntax, type-map
                             │ deltas, storage conventions, exporters
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ Pack libraries (versioned, optional, 1st- or 3rd-party)          │
│   Poly.Packs.Sql          canonical column/table + richer SQL    │
│   Poly.Packs.Oracle       Oracle defaults + optional ora sugar   │
│   Poly.Packs.SqlServer    …                                      │
│   Poly.Packs.PostgreSql   …                                      │
│   Poly.Packs.EfCore       (P5+ extract) DbContext / Use* wiring  │
│   (later) Poly.Packs.*    OpenAPI, PII labels, …                 │
└────────────────────────────┬────────────────────────────────────┘
                             │ implement small contracts only
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│ Poly core — DomainModeling plugin *seams* (no pack package refs) │
│   Facet IR · parse/print registries · type maps (SQL + CLR)      │
│   storage conventions · authoring pack set · analysis hooks later│
│   Domain → Syntax lowering unchanged (CORE: no domain opcodes)   │
└─────────────────────────────────────────────────────────────────┘
```

| Lives in core `Poly` | Lives in pack libraries | Lives in host |
|----------------------|-------------------------|---------------|
| Facet / `Annotation` IR | Vendor defaults (`Text`→`VARCHAR2`) | Pack registration list |
| Parse/print **hooks** + registries | Vendor sugar parsers (`ora`) | CLI flags (P1+); MCP params (P4+) |
| Storage fields (`ColumnName`, table name override, …) | Storage convention implementations | Which artifacts to emit |
| Fail-closed unknown-annotation policy | Pack-specific diagnostics | Discoverability UX |
| Authoring-context pack set (+ hash stub) | EF/DDL/OpenAPI exporters | File I/O, tool surface |
| Generic SQL **and** stable CLR default maps | Overrides for SQL maps (rarely CLR) | — |

**Dependency rule:** `Poly` ↛ packs. Packs → `Poly`. Hosts → `Poly` + selected packs.

**Façade note:** No required `IDomainPack` mega-interface until **after two real packs** (P4+). Until then packs are ordinary classes that register into small registries.

---

## 3. IR: portable annotations

### 3.1 Facets on `DomainType` and `Property`

```
DomainType (abstract)
  ├── Constraints: IReadOnlyList<Constraint>   ← validating (required, unique, range, …)
  ├── Facets: IReadOnlyList<Facet>             ← type-level metadata (table, schema, …)
  └── Properties: IReadOnlyList<Property>
        ├── Constraints                        ← validating at property level
        └── Facets                             ← property-level (column, pii, …)
```

`Entity`, `ValueType`, `PrimitiveType`, `EnumType` inherit type-level `Facets` from `DomainType`. Only `Property` gains its own `Facets` field.

**Scope of this plan:** author and consume facets on **Entity** (and properties) first. Other `DomainType` kinds may carry empty facet lists until a pack needs them.

`DomainType.Constraints` remains for rare validating type-level rules. **Do not** teach agents to put `table` / `column` in the constraint bag.

### 3.2 Grammar (P1-locked)

**Entity header (type-level facets)** — after kind keyword, before `{`:

```text
Name: entity { ... }                              // no facets
Name: entity table("ORDERS") { ... }
Name: entity table("ORDERS") schema("FINANCE") { ... }
```

- Facets are **not** free-standing statements inside the entity body in v1.
- Enum / value headers: no facet syntax in P1 product surface (IR allows empty lists).

**Property tail** — after type, mixed with built-in constraints:

```text
CardNumber: Text unique column("CARD_NBR", "VARCHAR2(20)")
Name: Text column("NAME")
Total: Number range(0, ) column("ORDER_TOTAL")
```

Order: built-in constraints and annotations may interleave; each token is either a known constraint keyword or a registered annotation keyword (fail-closed otherwise).

**`column` argument forms (positional only in P1):**

| Form | IR (`Annotation` args) |
|------|-------------------------|
| `column("NAME")` | `name` → `"NAME"` |
| `column("NAME", "VARCHAR2(20)")` | `name` → `"NAME"`, `type` → `"VARCHAR2(20)"` |

No keyword-argument syntax (`type: "..."`) in P1. Named-arg sugar may arrive later inside annotation parsers only.

**`table` (when enabled by Sql pack / test double):**

| Form | IR |
|------|-----|
| `table("ORDERS")` | `name` → `"ORDERS"` |

**Duplicate keywords:** two `column` (or two `table`) on the same target → **parse error** (fail-closed).

### 3.3 Facet vs Constraint

| Kind | Base type | Role | Runtime VM? |
|------|-----------|------|-------------|
| **Semantic constraint** | `Constraint : DomainObject` | `required`, `unique`, `range`, `length`, `pattern`, … | Often yes |
| **Annotation / facet** | `Facet : DomainObject` | `column`, `table`, `pii`, vendor sugar | No |

```csharp
// sketch — not rigid API commitment
public abstract record Facet : DomainObject;

/// <summary>Portable pack metadata. Args are literals only.</summary>
public sealed record Annotation(
    string Name,
    IReadOnlyDictionary<string, AnnotationValue> Arguments
) : Facet;

// Closed value set for equality + round-trip
public abstract record AnnotationValue;
public sealed record AnnotationString(string Value) : AnnotationValue;
public sealed record AnnotationNumber(double Value) : AnnotationValue;
public sealed record AnnotationBool(bool Value) : AnnotationValue;
public sealed record AnnotationNull : AnnotationValue;
```

Vendor packs must **not** add per-DBMS types to core (`OraColumnConstraint` rejected). Sugar desugars to `Annotation("column", …)` (or pack-private name that still prints via that pack).

### 3.4 Storage projection (core fields packs fill)

| Field | Purpose |
|-------|---------|
| `StorageColumn.ColumnName` | DB column id (default: property name). **New** — today `Name => Source.Name` only |
| `StorageColumn.ColumnType` | Native type string (default: SQL type map). Exists; **under-consumed** by EF codegen |
| `StorageEntity` table name | Override from `table` facet (default remains pluralization convention) |
| MaxLength / nullability / unique | Already partially present |

`StorageEntity` / `StorageColumn` are **classes** — use rebuild helpers or migrate to records; do not assume `with { }`.

EF path (`DbContextGenerator` as `IDomainTargetExporter` adapter) must **read** `ColumnName` / `ColumnType` / table name and emit `HasColumnName` / `HasColumnType` / `ToTable`. No string-patch decorators.

### 3.5 Evolution and call-site blast radius (P1.1)

P1 is not a 20-line IR tweak. Expect:

| Work | Notes |
|------|-------|
| `DomainType` + `Property` signature / `Children` | Include `Facets` |
| `AddFacetToPropertyChange` / `AddFacetToDomainTypeChange` | Parallel constraint changes |
| Builder + evolution fluent helpers | If present for constraints |
| Call sites | Parser, examples, CLR bootstrap, exporters, tests — mechanical compile fix |
| Printer | Header facets + property-tail facets |

---

## 4. Core seams (minimal contracts)

### 4.1 Authoring context

```csharp
// sketch
public sealed class DomainAuthoringContext {
    public DomainPackSet Packs { get; }     // ordered; hash stub for later MCP reproducibility
    public TypeMappingRegistry TypeMaps { get; }
    // annotation parse/print registries derived from pack contributions
}
```

- `new PolyDslParser(text)` = **core-only** product surface (no pack annotations).
- Pack-aware: `new PolyDslParser(text, authoringContext)` (or equivalent factory).
- **P1–P3:** DslCompiler + tests supply context. MCP stays core-only (D4).
- **P4+:** MCP session records pack set id/hash; `apply_dsl` / `export_dsl` share the same set.

### 4.2 Annotation parse / print

```csharp
public interface IAnnotationSyntax {
    string Keyword { get; }   // "column", "table", "ora", …
    bool TryPrint(Facet facet, out string text);
}
```

**P1 reality (rev 4):** Core `PolyDslParser` natively parses positional
`keyword(arg, …)` into portable `Annotation` records after the keyword is
accepted by `AnnotationRegistry`. Packs implement **print** (and later custom
facet types). A pack-owned `IDslTokenReader` + `Parse` hook is deferred until a
real second grammar (vendor sugar such as `ora(…)`) needs non-native parsing —
do not land dead reader surface “for the future.”

**Fail-closed (D2):**

- Unknown / unregistered `keyword(…)` at parse → explicit error (entity header and property tail).
- Trailing comma / non-separator junk in annotation args → error.
- Facet present at print with no handling syntax → error (never `/* unknown */`).
- Test/print handlers must not invent placeholder text for malformed args.
- Escape later: explicit **passthrough** pack that preserves generic `Annotation` text.

### 4.3 Type mapping registry (D3 + D5)

Split maps:

| Map | Core default (D3 / stable) | Pack role |
|-----|----------------------------|-----------|
| **SQL column types** | Generic SQL: e.g. `Text`→`varchar`, `Number`→`bigint`, `Boolean`→`boolean`, `Date`→`date`, `DateTime`→`timestamp` (exact strings locked in P2 tests) | Vendor packs override per key |
| **CLR type names** | Current `ToClrTypeName` behavior (`string`, `long`, …) | Rarely overridden |

**Conflict rule (D5):** merge-override registry. Each pack contributes a **delta**. Store overrides **newest-first**; lookup returns **first hit** for a key (equivalent to last-registered wins). Unmentioned keys fall through to older packs, then core defaults.

Do **not** bake SQL Server `nvarchar`/`datetime2` as permanent core defaults.

### 4.4 Storage convention

```csharp
public interface IStorageConvention {
    StorageColumn ProjectColumn(Property property, StorageColumn baseline);
    StorageEntity ProjectEntity(Entity entity, StorageEntity baseline);
}
```

Ordered chain after baseline `StorageAnalyzer` build. Applies `column` / `table` facets and engine defaults.

### 4.5 Target export (host adapter — not core VM)

```csharp
public interface IDomainTargetExporter {
    string Id { get; }   // "ef-core.dbcontext", …
    IReadOnlyList<GeneratedFile> Export(
        Domain domain, InfrastructureModel model, DomainAuthoringContext ctx);
}
```

**D7:** `DbContextGenerator` (and siblings) stay in `Poly.DslCompiler` as adapters implementing this contract. Extract `Poly.Packs.EfCore` only when a second host needs the same exporter (P5).

### 4.6 Analysis contributions (later)

Pack analyzers (duplicate physical column names, identifier length, …) after P2. No VM opcodes; no emitter forks (CORE).

---

## 5. Multi-DBMS pack map (illustrative)

| Library | Adds | Depends on | When |
|---------|------|------------|------|
| **Core Poly** | Facet IR, hooks, storage fields, generic SQL + CLR maps, pack set | — | P1–P2 |
| **Test double** (tests only) | Temporary `column` (± `table`) for round-trip | Poly | P1 only |
| **Poly.Packs.Sql** | **Canonical** `column` / `table` syntax + richer portable defaults | Poly | P3 — **replaces** test double as owner of those keywords |
| **Poly.Packs.Oracle** or **SqlServer** | Vendor type map, diagnostics, optional sugar | Poly (+ Sql recommended) | P4 |
| **Poly.Packs.PostgreSql** etc. | Same pattern | Poly + Sql | P5 pull |
| **Poly.Packs.EfCore** | Shared DbContext exporter | Poly | P5 if second host |
| **Third-party** | Same contracts | Poly | Anytime after seams stable |

**Composition example (host, P4+ shape):**

```csharp
var ctx = DomainAuthoringContext.Create()
    .Add(new SqlMappingPack())        // Poly.Packs.Sql
    .Add(new OracleMappingPack())     // Poly.Packs.Oracle
    .Build();

var compiler = new DslCompiler(ctx);  // EF generators remain in-process adapters (D7)
```

Domain text stays portable with `column` / `table`. Selecting Oracle changes **defaults and codegen**, not entity/action/stage semantics.

---

## 6. Motivating vertical (Oracle as consumer)

**Authoring (portable):**

```poly
domain Library

Patron: entity table("PATRON_MASTER") {
  CardNumber: Text unique column("CARD_NBR", "VARCHAR2(20)")
  Name: Text column("NAME")
}
```

**Packs:** Sql + Oracle; host runs EF adapter  

**Expected EF fragment:**

```csharp
modelBuilder.Entity<Patron>(b => {
    b.ToTable("PATRON_MASTER");
    b.Property(x => x.CardNumber)
        .HasColumnName("CARD_NBR")
        .HasColumnType("VARCHAR2(20)");
    b.Property(x => x.Name)
        .HasColumnName("NAME");
});
```

**Proof obligations:**

1. Round-trip parse/print with Sql (or P1 test double) enabled — idempotent.
2. Without pack: `column` / `table` fail at parse.
3. Print-without-pack of a domain that already has facets → error (not silent drop).
4. Storage snapshot: `ColumnName` / `ColumnType` / table name overrides.
5. EF adapter emits name/type/table (not only max length).
6. Swap Oracle → SqlServer pack: same domain without explicit type arg gets different default `ColumnType`; explicit type arg preserved.

---

## 7. Phase plan

### P0 — Decisions — **DONE**

| ID | Decision |
|----|----------|
| D1 | Separate `Facet` base type; `DomainType.Facets` + `Property.Facets`; `Annotation` concrete record |
| D2 | Unknown annotation → **fail-closed**; explicit passthrough pack escape |
| D3 | Core SQL map = **generic SQL**; packs override; CLR map stays separate/stable |
| D4 | MCP pack enablement **deferred to P4+**; compiler-only P1–P3 |
| D5 | **Newest-first / first-hit** per-key merge-override |
| D6 | First syntax = **`column(...)`** positional; `table(...)` second; vendor sugar last |
| D7 | EF stays in DslCompiler as **`IDomainTargetExporter` adapter**; extract on second host |

### P1 — Annotation IR + parse/print hooks (core) ✅

| Step | Deliverable | Status |
|------|-------------|--------|
| 1.1 | `Facet` / `Annotation` (+ closed `AnnotationValue`) on `DomainType` + `Property`; `Children`; evolution changes; builder/call-site compile green | Done |
| 1.2 | `AnnotationRegistry` + `DomainAuthoringContext`; keyword registration fail-closed on duplicates | Done (`IDslTokenReader` deferred — see §4.2) |
| 1.3 | Parser: **entity header** facets after `entity`; **property tail** (primitive **and** enum-typed); unregistered `keyword(…)` error; positional args only | Done |
| 1.4 | Printer: header + property facets via registry; missing handler → error | Done |
| 1.5 | **Test-only** double pack: `column` + `table` — not shipped product surface | Done |
| 1.6 | Tests: round-trip; parse/print without pack fails; trailing comma; content equality; malformed print | Done |

**Non-goals:** EF output, MCP guide listing `column`, vendor sugar, product `poly-dsl-guide` changes (guide stays core-only).

### P2 — Storage fields + convention chain (core) ✅

| Step | Deliverable | Status |
|------|-------------|--------|
| 2.1 | `StorageColumn.ColumnName`; entity table-name override field/API | Done |
| 2.2 | `TypeMappingRegistry` (SQL + CLR) threaded through infrastructure/storage analysis; core generic SQL defaults locked by tests | Done |
| 2.3 | `IStorageConvention` chain after baseline build | Done |
| 2.4 | Tests: annotation → storage snapshot (column name, type, table name, enum, convention chain) | Done |

### P3 — First real pack library + host wiring ✅

| Step | Deliverable | Status |
|------|-------------|--------|
| 3.1 | Canonical `column`/`table` (`ColumnAnnotationSyntax`/`TableAnnotationSyntax` in core); `CreateWithSqlPack()` factory; retire test-only doubles | Done |
| 3.2 | `DslCompiler` creates `DomainAuthoringContext` with Sql pack, threads to parser + `InfrastructureAnalyzer` | Done |
| 3.3 | `DbContextGenerator` emits `b.ToTable(…)` always, `HasColumnName(…)` always (camelCase default/annotation override), `HasColumnType(…)` always | Done |
| 3.4 | 9 tests: default plural, annotation overrides, camelCase, required, max length, shadow key, unique key dedup | Done |

### P4 — Second engine pack (proves multi-DBMS)

| Step | Deliverable | Status |
|------|-------------|--------|
| 4.1 | Choose first second-engine pack | Done — **SQLite first shippable** (no server); SqlServer also present |
| 4.2 | Type-map overrides (+ identifier diagnostics where relevant) | Done (Sqlite + SqlServer) |
| 4.3 | Same domain, two pack sets → different defaults; explicit column type stable | Done |
| 4.3b | **Host composition** — `DslCompiler` / CLI `--dbms generic\|sqlite\|sqlserver` | Done |
| 4.4 | Optional vendor sugar → desugar to `column` | Not started |
| 4.5 | MCP pack enablement + guide honesty for enabled packs | **Done** — shared `McpAuthoring.Context` (Sql pack) wired into `apply_dsl` and `export_dsl`; `poly-dsl-guide.md` documents `column`/`table` as product surface. Session pack-set hash deferred to second consumer (static context for now). |

**After P4:** optional `IDomainPack` convenience façade; third-party template docs.

### P5 — Pull only

- More engines (PostgreSQL, SQLite, MySQL, …)
- Extract `Poly.Packs.EfCore` if a second host needs it
- Assembly load / NuGet marketplace only with real external consumers

### Out of this plan

| Item | Why |
|------|-----|
| Actors / Orleans | Separate experiment |
| Plugin-defined VM opcodes | CORE forbid |
| MEF / open assembly catalog as v0 | Working packs before plugin host |
| String-source decorators | Structured export only |
| Per-DBMS constraint types in core | Pack libraries only |
| Relationship / Action / Stage facets | Defer until DomainType+Property proven |
| Keyword-arg annotation syntax | P1 uses positional only |

---

## 8. Relationship to `InfrastructureModel`

Keep the five-slice model (topology, aggregate, behavior, storage, transport) as the **shared fact base**. Packs should:

- **prefer** filling storage conventions and type maps  
- **not** fork parallel “OracleInfrastructureModel” trees in core  

Extra pack facts: analysis metadata or pack-private bags — do not explode `InfrastructureModel` with vendor-only properties.

Pluralization (`Name + "s"`) remains a separate convention concern; `table("…")` is the explicit override.

---

## 9. Risks

| Risk | Mitigation |
|------|------------|
| Plugin soup / empty registries (AP-003) | No registry without a consumer test; no `IDomainPack` until two packs |
| SQL Server defaults stuck in core | D3: generic SQL in core; vendor packs override; tests lock default strings |
| MCP/`apply_dsl` accepts annotations compiler-only path doesn’t | D4: MCP core-only until P4; document honesty |
| Agents invent `ora` / `column` from lab docs | Product guide stays core-only until packs on MCP; parse fails otherwise |
| Facet treated as validation | Separate IR; analyzers only enforce known semantic constraints |
| Test double and Sql pack both own `column` forever | P3 makes Sql pack canonical; remove double from product paths |
| Third-party version skew | Pack set hash (stub P1, real P4); lock host references early |
| Under-scoped P1 (`Property` signature blast radius) | §3.5 checklist; full compile + suite green |

---

## 10. Success criteria

- [x] P0 decisions written (§11)  
- [x] P1: portable annotation round-trip with test pack; fail-closed parse **and** print without pack  
- [x] P2: storage carries `ColumnName` / `ColumnType` / table override from annotations + maps  
- [x] P3: Sql pack is canonical `column`/`table` owner; EF adapter emits name/type/table  
- [x] P4: multi-DBMS packs + host wiring  
  - [x] SQLite first shippable pack (`src/Poly.Packs.Sqlite/`)  
  - [x] SqlServer pack (`src/Poly.Packs.SqlServer/`)  
  - [x] Host composition: `DslCompiler` `DbmsPack` + CLI `--dbms`  
  - [ ] P4.4: vendor sugar (desugar to `column`)  
  - [x] P4.5: MCP pack enablement — `apply_dsl`/`export_dsl` Sql pack wired; guide updated  
- [x] Core `Poly` has **zero** references to Oracle/SQL Server/Npgsql packages (packs are separate libraries)  
- [ ] CORE path intact: domain execution still lowers to existing Syntax only  

---

## 11. Decision log

| Date | Decision | Notes |
|------|----------|-------|
| 2026-07-18 | Prior experiment parked | docs/experiments/domain-plugin-extension-platform.md |
| 2026-07-21 | v1 plan drafted | Over-rotated on IDslPlugin + ora-in-core; reviewed |
| 2026-07-21 | **v2 reframed** | Core seams; multi-DBMS packs as libraries; portable `column` first |
| 2026-07-21 | **D1: separate `Facet` base type** | `DomainType.Facets` + `Property.Facets`. `Annotation` + closed arg values. Not `Constraint`. |
| 2026-07-21 | **D2: unknown annotation → fail-closed** | Parse/print error unless pack registered. Escape: explicit passthrough pack. |
| 2026-07-21 | **D3: core SQL type map = generic SQL** | e.g. `varchar` / `bigint` / `date` / `timestamp` — no nulls, no vendor bias. CLR map separate. Packs override SQL keys. |
| 2026-07-21 | **D4: MCP pack enablement deferred to P4+** | Compiler-only for P1–P3. Product guide stays core-only until then. |
| 2026-07-21 | **D5: pack conflict = newest-first first-hit** | Per-key merge-override; last registered wins. |
| 2026-07-21 | **D6: first syntax = positional `column(...)`** | `column("N")` / `column("N","T")`. `table("T")` second. Vendor sugar last. No `type:` kwargs in P1. |
| 2026-07-21 | **D7: EfCore stays in DslCompiler as adapter** | `IDomainTargetExporter`. Extract to `Poly.Packs.EfCore` only when a second host appears. |
| 2026-07-21 | **rev 3 consistency** | Grammar, blast radius, D3/D5 wording, P3 handoff, entity table field, duplicate-keyword fail-closed |
| 2026-07-21 | **rev 4 P1 code review** | Native parse (no dead `IDslTokenReader`); clear unregistered-annotation errors; enum property tails; trailing-comma reject; content equality for `Annotation`; print handlers fail closed on bad args |
| 2026-07-21 | **rev 5 P2 code review** | D3 generic SQL defaults in `DomainTypeMapping` (not SQL Server); registry is override layer only; empty column/table names fail closed; last annotation wins; authoring context owns type maps + convention chain; InfrastructureAnalyzer threads authoring |
| 2026-07-21 | **P3: Sql pack + EF emission** | Canonical `ColumnAnnotationSyntax`/`TableAnnotationSyntax` in core `Poly/DomainModeling/`; `CreateWithSqlPack()` factory; `DbContextGenerator` emits `ToTable`/`HasColumnName`/`HasColumnType` from `StorageModel`; 9 new tests; 1518→1527 green |
| 2026-07-21 | **P4.1–4.3: SqlServer pack** | `Poly.Packs.SqlServer` library (separate project, no core deps); `SqlServerDefaults.AddSqlServerDefaults()` extension; type-map overrides (nvarchar/bit/datetime2/uniqueidentifier); `SqlServerIdentifierConvention` (128-char limit); 11 tests proving multi-DBMS defaults differ and explicit annotations stable; 1530→1541 green |
| 2026-07-21 | **P4 shippable: SQLite + host** | `Poly.Packs.Sqlite` first shippable pack (TEXT/INTEGER/REAL/BLOB affinities; no service); `DslCompiler.DbmsPack` + `CreateAuthoring` + CLI `--dbms generic\|sqlite\|sqlserver`; host EF DbContext golden tests prove generic vs sqlite differ; 1541→1552 green |
| 2026-07-21 | **P4.5: MCP pack enablement** | `McpSessionStore` gains shared `McpAuthoring` static (Sql pack); `apply_dsl` passes `Context` to parser; `export_dsl` passes `Annotations` to printer; `poly-dsl-guide.md` adds `column`/`table` syntax + annotations section. Agents can now use storage annotations in MCP. Suite 1552 green (stable). |

---

## 12. Bottom line

**Yes:** core plugin **architecture** (seams + IR + authoring context) belongs in DomainModeling.  
**Yes:** Oracle and every major DBMS should be enableable as **libraries** over time, including third-party.  
**Yes:** facets on **types and properties**.  
**No:** core does not embed vendor types or EF/Oracle packages.  
**No:** full plugin host / actors / MEF before portable annotation + storage + Sql pack + second engine pack prove the seams.

**P0 + P1 + P2 + P3 + P4 host multi-DBMS (SQLite first shippable) complete.** Next when prioritized: **P4.4–4.5 → P5**. Everything else is pull-only.
