# Infrastructure Pass Suite — Stress Tests

**Date:** 2026-07-22  
**Status:** Exploratory — not implementation plans  
**Related:** [`docs/plans/archive/infrastructure-pass/infrastructure-concern-analyzer-suite.md`](../plans/archive/infrastructure-pass/infrastructure-concern-analyzer-suite.md) (archived suite)

> These thought experiments validate the pass-suite design against hypothetical future
> targets. Nothing here should be built until a real consumer exists (AGENTS §6).
> The migration ladder in the plan document covers only the steps grounded in today's
> shipped code.

---

## 1. TypeScript library target

This section stress-tests the pass-suite design by mapping what a TypeScript library target would need. If the design cleanly accommodates a fundamentally different output language, the abstraction is right.

### 1.1 Shared passes (language-agnostic, run once per domain)

These passes produce metadata that a TypeScript target consumes **without changes**:

| Pass | What it produces | Why it's language-agnostic |
|------|------------------|----------------------------|
| `DomainModelAnalysisPipeline` | Entity structure, types, constraints, effects, subscriptions | The *shape* of the domain — entities, properties, stages, actions — is independent of output language |
| `EffectTopologyPass` | Cross-entity create-in, invoke, subscriptions | Coupling topology is a domain fact, not a rendering concern |
| `OwnershipAggregatePass` | Root/child hierarchy, aggregate parent | Ownership exists in the model; TS and C# consumers need the same hierarchy for REST nesting |
| `BehaviorPass` | Action signatures, parameters, return types, policies | A `CheckOut(book: Book) → Loan` action has the same signature whether rendered in C# or TS |
| `StoragePass` (logical half) | `StoreName`, `FieldName`, `KeyName`, navs, FKs | ORMs in any language need the same logical storage shape. `PhysicalTypeOverride` is only used by SQL-dialect renderers |
| `TransportPass` | Resource hierarchy, exposability | REST routes are the same regardless of server framework |
| `RestApiSurfacePass` | REST routes, DTOs, seed hints | REST-specific — Express and ASP.NET share it; GraphQL/gRPC skip it |

**The analysis hub does not know or care about TypeScript.** These seven passes run once and produce metadata any target can consume.

### 1.2 What changes for TypeScript

#### A — Type mapping (a target-pack responsibility)

Domain primitive types → TypeScript types are different from → CLR types:

| Domain type | CLR (C#) | TypeScript |
|------------|----------|------------|
| `Text` | `string` | `string` |
| `Number` | `long` | `number` |
| `Boolean` | `bool` | `boolean` |
| `DateTime` | `DateTime` | `Date` (or `string` for API) |
| `Date` | `DateOnly` | `string` (ISO date) |
| `Guid` | `Guid` | `string` |
| `Binary` | `byte[]` | `Uint8Array` \| `string` (base64) |
| `Decimal` | `decimal` | `number` (or library type) |

**Decision:** Keep it a target-pack responsibility (no new analysis pass needed). The `DomainToCSharpExporter` already maps domain primitives to CLR type names via `DomainTypeMapping.ToClrTypeName()`. A `DomainToTypeScriptExporter` would do the same mapping to TS types. The AnalysisResult metadata already carries the *domain* type names, not the CLR type names, so the TS renderer has the input it needs.

Exception: if many targets (TS, Python, Kotlin, …) need a shared "domain type → generic language type" map, extract a `TypeMappingPass` that normalizes to an intermediate representation (`INTEGER`, `STRING`, `FLOAT`, `DATE`, `BOOLEAN`, `BINARY`, …) that each target pack then maps to its own syntax.

#### B — Module graph (potentially new)

TypeScript projects use modules (`import`/`export`). The analyzer could compute a **dependency graph** among entities to determine file-split boundaries, import chains, and circular dependency detection.

**Decision:** Don't add this pass until a second target (Python, Kotlin, …) also needs module/import metadata. For a single TS pack, the file-split and import logic can live in the target pack itself. If the TS pack is the **first consumer** and the module logic proves generally useful, extract it as a pass.

#### C — Serialization shape (optional, post-v1)

TypeScript/JavaScript handles serialization differently (`DateTime` → ISO string, enums → string unions or numeric, `null` vs `undefined`). A `SerializationPass` could annotate properties with serialization hints consumed by both a TS API client generator and a TS Zod validation schema generator.

**Decision:** Defer. Start with simple conventions in the TS renderer. Extract when two renderers must agree on serialization shape.

### 1.3 The TypeScript target pack composition

```text
Domain analysis (shared hub)
  + infrastructure passes (shared, language-agnostic)
        │
        ▼
  TypeScript target pack (Poly.Packs.TypeScript or similar)
        │
        ├── DomainToTypeScriptExporter (entity types, enums, DomainResult equivalent)
        │     └── syntax nodes → TypeScriptGenerator (parallel to CSharpGenerator)
        │
        ├── ORM schema exporter (Prisma / TypeORM / Drizzle)
        │     └── consumes StorageMappingMetadata logical fields
        │
        ├── API router exporter (Express / Fastify / NestJS)
        │     └── consumes RestApiMetadata (REST) or BehaviorPass (GraphQL)
        │
        ├── Validation schema exporter (Zod / class-validator)
        │     └── consumes EntityStructureMetadata (constraints)
        │
        └── API client library (optional downstream)
              └── consumes RestApiMetadata (REST endpoints) or BehaviorPass (GraphQL ops)
```

### 1.4 What this tells us about pass design

1. **Seven of eight passes need zero changes** — they're language-agnostic analysis that produces metadata the TS pack consumes.
2. **The one potentially-new pass (ModuleGraph)** can live in the target pack until a second consumer demands extraction (§6).
3. **Type mapping is a rendering concern**, not an analysis pass — `DomainToCSharpExporter` maps to CLR; a TS equivalent maps to TS types. No new pass.
4. **The Syntax IR is genuinely language-agnostic** — a `TypeScriptGenerator` would walk the same node types that `CSharpGenerator` walks, emitting different text.
5. **The StoragePass's logical/physical split is validated** — a TypeScript ORM pack needs table/column/nav/FK metadata but ignores `PhysicalTypeOverride`.

---

## 2. Rust library + NoSQL database

This section stress-tests the pass suite with a **full 180° turn**: a Rust target (no GC, no reflection, `Result<T, E>`, `Option<T>`) combined with a document NoSQL database (MongoDB / DynamoDB — no tables, columns, or foreign keys).

### 2.1 What works unchanged (the analysis hub is solid)

| Pass | Status | Why it survives |
|------|--------|----------------|
| `DomainModelAnalysisPipeline` | ✅ Unchanged | Entity structure, constraints, effects, stages — same domain |
| `EffectTopologyPass` | ✅ Unchanged | Cross-entity coupling is domain-level, not language-level |
| `OwnershipAggregatePass` | ✅ Unchanged | Root/child ownership drives document nesting or separate collections |
| `BehaviorPass` | ✅ Unchanged | `CheckOut(book: Book) → Loan` has the same signature in Rust |
| `CrossReferencePass` | ✅ **More important** | Rust has no ORM to paper over cycles — initialization order is a hard constraint |
| `RestApiSurfacePass` | ✅ Unchanged (REST-specific) | REST routes and DTOs; GraphQL/gRPC use base passes directly |

### 2.2 What fragments and why

#### A—StoragePass naming is SQL-centric (the logical fields are still useful)

| StorageModel field | Rust + NoSQL interpretation | Natural fit? |
|-------------------|-----------------------------|:------------:|
| `TableName` | Collection name in MongoDB / table name in DynamoDB | ⚠️ SQL-biased name |
| `Columns[].ColumnName` | Document field name / serialization key | ⚠️ SQL-biased name |
| `Columns[].ClrTypeName` | → target type mapping | ✅ Neutral |
| `KeyName` / `KeyProperty` | Document `_id` or partition key | ✅ Neutral |
| `CollectionNavigations` | Embedded document (`Vec<Loan>`) or reference array | ✅ Neutral |
| `ForeignKeys` | **Ignored** — NoSQL has no referential integrity | N/A |
| `ColumnType` | **Ignored** — NoSQL has no column types | N/A |

**Insight:** The *logical* storage fields are genuinely useful for NoSQL, but the *naming* assumes a relational mental model. Rename to `StoreName`/`FieldName`/`PersistentField`/`CrossStoreReference` (see plan Step 5.1).

#### B—Rust has no reflection: serialization metadata is essential

Rust requires explicit serialization metadata because `serde::Serialize`/`serde::Deserialize` must be derived with field-level attributes. A `SerializationPass` would produce `SerializationMetadata` — per-field naming, format, optionality, enum strategy.

**Recommendation:** Reserve the pass ID `InfraSerialization` in the pipeline constants, document its shape, but do not implement until a Rust pack (or a second consumer like OpenAPI schema export) forces it.

#### C—Program structure: Rust is not class-based

The Syntax IR is still the right intermediate representation. But the `DomainProgramProjection` needs to produce **neutral program IR**, and each target pack then decorates for its idiom. This is the exporter split described in the plan's Step 5.3.

#### D—Ownership model: no GC means no shared mutable references

This is not a pass gap — it's an **effect-lowering architecture** issue. The Syntax IR for effects is fine; the Rust renderer must emit idioms that respect ownership. `CrossReferencePass` becomes *more* important here.

### 2.3 Summary

The Rust+NoSQL exercise validates the design with three corrections:

1. **No new base passes are needed.** NoSQL-specific projection belongs in a pack.
2. **StoragePass naming must be de-relationalized.** (Already in Step 5.1.)
3. **The serialization pass is essential for Rust but still §6-deferred.**
4. **The exporter split is validated and more important.** The shared `DomainProgramProjection.ToSyntax()` layer is essential before a second target language can be productive.

---

## 3. Authorization — a native domain dimension

Authorization changes the domain vocabulary itself — not just a new metadata projection. It requires extending the domain model with actors, roles, and identity-aware policy evaluation. The design exists in [`DOMAIN-DSL-SPEC.md`](DOMAIN-DSL-SPEC.md).

### 3.1 Policy evaluation IS authorization

The design unifies business preconditions and authorization through `require PolicyName`:

```swift
// Policy on a regular entity — evaluates against the entity
HasStock: policy { QuantityOnHand > 0 }

// Policy on an actor entity — evaluates against the actor
Warehouse: policy { role is "Warehouse" }

// Both referenced the same way in require
Ship: action when Submitted
  require HasStock, Employee.Warehouse
```

### 3.2 What an AuthorizationPass contributes

```csharp
public sealed record AuthorizationMetadata(
    IReadOnlyList<PolicyBinding> PolicyBindings,
    IReadOnlyList<ActorDefinition> Actors,
    IReadOnlyList<OwnershipRule> DataOwnership,
    IReadOnlyList<string> PublicActions
) : IAnalysisMetadata;
```

Per-target artifacts:

| Target | Artifact |
|--------|----------|
| ASP.NET Core/MinApi | `[Authorize(Policy = "...")]` on endpoints |
| GraphQL (HotChocolate) | `@authorize(policy: "...")` directive on resolvers |
| Rust (Actix) | `middleware::from_fn(check_role)` extractors |
| OpenAPI | `security` section with OAuth scopes per path |

### 3.3 Actor as a pack — validating every extension point

Core needs **three small changes**:

| Change | File | Lines |
|--------|------|:-----:|
| Entity type keyword registry | `DomainAuthoringContext.cs` | ~10 |
| Parser: accept registered keyword | `PolyDslParser.cs` | ~5 |
| Printer: emit correct keyword | `DomainDslPrinter.cs` | ~5 |
| Actor pack (new) | `src/Poly.Packs.Auth/` | ~150 |
| AuthorizationPass (new) | pack or core? | ~200 |

The actor pack is the strongest validation the pass suite could receive — it exercises parser extensibility, printer extensibility, analysis pass contribution, and cross-phase metadata consumption through seams designed for storage packs.

---

## Gap summary

| Gap | Status | Action |
|-----|--------|--------|
| `SerializationPass` | **Design now, implement when Rust pack exists** (§6) | Reserve `InfraSerialization`; document shape |
| Storage vocabulary rename | **In plan Step 5.1** | `TableName` → `StoreName`, etc. |
| Domain→Syntax exporter split | **In plan Step 5.3** | `DomainProgramProjection.ToSyntax()` as shared layer |
| NoSQL storage projection | **Pack pass, not base** | MongoDB/DynamoDB pack adds its own `INodeAnalyzer` |
| Constraint projection | **Defer** (§6) | Wait for 4th consumer |
| Module/import graph | **Defer** (§6) | Wait for 2nd module-aware target |
