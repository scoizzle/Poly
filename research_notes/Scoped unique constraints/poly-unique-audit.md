# Poly `unique` — current implementation audit and the seams a scoped `unique_within(parent)` would touch

Revision audited: `aa142413` (`refactor(mcp): name the workspace lifecycle tool poly_workspace`), 2026-11-06.
All paths are repo-relative to the Poly worktree. Line numbers are as of that revision.

Scope note: this is a read-only code audit. Every claim below was read from the working tree; nothing was built or executed. Where a claim is an inference it is labelled as such.

---

## Q0. File-and-line inventory of the whole `unique` path

### Takeaway
`unique` is a memberless constraint record whose only job is to be *recognised* by four consumers: the DSL parser/printer, the analysis passes that exclude it from static checking, the storage-model projection (`StorageColumn.IsUnique`), and the storage-owned `IConstraintCompiler` that injects an `IStoragePort` guard into the lowered module. There is no dedicated unique runtime: it is three small `Invoke`s (`db.Exists`, `db.Release`, `db.Record`) inside the normal module, plus a session-scoped key set and an EF unique index.

### Cited Findings

**Ontology**
- `UniqueConstraint` is `public sealed record UniqueConstraint : Constraint;` with no members — `[Poly/DomainModeling/Ontology/Constraints/UniqueConstraint.cs:3](Poly/DomainModeling/Ontology/Constraints/UniqueConstraint.cs#L3)`.
- Base `Constraint` is an empty abstract record — `[Poly/DomainModeling/Ontology/Constraint.cs:6](Poly/DomainModeling/Ontology/Constraint.cs#L6)`.
- Constraint merge has no `UniqueConstraint` arm, so merging two uniques returns `null` — `[Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs:13-26](Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs#L13)`.

**Parsing**
- Token kind — `[Poly/DomainModeling/Language/DslTokenKind.cs:55](Poly/DomainModeling/Language/DslTokenKind.cs#L55)`.
- Keyword map `"unique" => DslTokenKind.Unique` — `[Poly/DomainModeling/Language/DslTokenReader.cs:208](Poly/DomainModeling/Language/DslTokenReader.cs#L208)`.
- Canonical printer text `DslTokenKind.Unique => "unique"` — `[Poly/DomainModeling/Language/DslGrammar.cs:85](Poly/DomainModeling/Language/DslGrammar.cs#L85)`.
- Resolver arm, the only construction site: `case TokenKind.Unique: Advance(); return new UniqueConstraint();` — `[Poly/DomainModeling/Language/DomainResolver.cs:1494-1496](Poly/DomainModeling/Language/DomainResolver.cs#L1494)`.
- Constraint tail loop that attaches it to a property — `[Poly/DomainModeling/Language/DomainResolver.cs:392-403](Poly/DomainModeling/Language/DomainResolver.cs#L392)`; value-type properties use the same loop at `[DomainResolver.cs:1166-1172](Poly/DomainModeling/Language/DomainResolver.cs#L1166)`.
- `unique` in the constraint-kind predicate — `[DomainResolver.cs:1636-1641](Poly/DomainModeling/Language/DomainResolver.cs#L1636)`.
- The grammar table does **not** model constraints at all: the `property` pattern is only `Identifier : primitive-type` — `[Poly/DomainModeling/Language/DslGrammar.cs:170-172](Poly/DomainModeling/Language/DslGrammar.cs#L170)`. Constraints are hand-parsed by `DomainResolver.ParsePropertyTail`.

**Printing / presentation**
- `UniqueConstraint => "unique"` in the DSL printer — `[Poly/DomainModeling/Language/DomainDslPrinter.cs:873-884](Poly/DomainModeling/Language/DomainDslPrinter.cs#L873)`; `AppendPrintedConstraints` at `:864-871`.
- Query-layer constraint text (used by MCP read tools) — `[Poly/DomainModeling/Queries/DomainPresentation.cs:22-38](Poly/DomainModeling/Queries/DomainPresentation.cs#L22)`, unique at `:32`.
- MCP `add kind=constraint type=Unique` — `[Poly.Mcp/Tools/DomainTools.cs:1195-1210](Poly.Mcp/Tools/DomainTools.cs#L1195)`, format at `:1224-1232`.

**Analysis**
- `ConstraintValidation.IsSatisfiedBy(UniqueConstraint) => true` ("cannot validate statically; requires instance set") — `[Poly/DomainModeling/Analysis/ConstraintValidation.cs:19](Poly/DomainModeling/Analysis/ConstraintValidation.cs#L19)`; `Describe` at `:33`.
- `ConstraintQualityAnalyzer` runs the constraint checks, and its joint-satisfiability pass groups constraints **by CLR type** and calls `Merge` — `[Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs:91-108](Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs#L91)`.
- `EffectAnalyzer` validates literal/derived assigns through `ConstraintValidation.IsSatisfiedBy` only — `[Poly/DomainModeling/Analysis/EffectAnalyzer.cs:1315-1324](Poly/DomainModeling/Analysis/EffectAnalyzer.cs#L1315)`. No unique-specific arm exists.
- Natural key: first property carrying `UniqueConstraint` — `[Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:44-52](Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs#L44)`; unique forces non-empty in `EmptyIsLegal` at `:197-199`.
- Published structure facts `HasNaturalKey` / `KeyPropertyName` / `KeyClrType` — `[Poly/DomainModeling/Analysis/EntityStructureMetadata.cs:17-24](Poly/DomainModeling/Analysis/EntityStructureMetadata.cs#L17)`.
- Storage projection: `keyProperty`/`keyName`/`keyClrType` from the natural key — `[Poly/DomainModeling/Analysis/StorageAnalyzer.cs:183-207](Poly/DomainModeling/Analysis/StorageAnalyzer.cs#L183)`; `IsUnique` column flag at `:305`; FK parent key at `:393-425`.
- `StorageColumn.IsUnique` — `[Poly/DomainModeling/Analysis/StorageModel.cs:125,143,173](Poly/DomainModeling/Analysis/StorageModel.cs#L125)`.
- Constraint-check coverage ledger names the storage owner for `unique` — `[Poly/DomainModeling/Lowering/ConstraintCheckWriter.cs:255-268](Poly/DomainModeling/Lowering/ConstraintCheckWriter.cs#L255)`.
- `docs/CORE.md` states the contract: "The storage libraries own `unique`: the guard calls the in-process `IStoragePort` (`Exists` = pending key or database), and the post-construction call records the pending key" — `[docs/CORE.md:132](docs/CORE.md#L132)`; coverage-line rule at `:134`.

**Lowering**
- Assign lowering wraps uniquely-assigned properties in `this.EnsureUnique(name, value)` — `[Poly/DomainModeling/Lowering/EffectLoweringPass.cs:242-264](Poly/DomainModeling/Lowering/EffectLoweringPass.cs#L242)` and `:378-406`; `IsUniqueProperty` prefers `StorageMappingMetadata` columns and falls back to the property constraint — `:359-372`.
- `UniqueConstraintCompiler` (the storage library's `IConstraintCompiler`) — `[Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs:1-369](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L1)`.
- Registered by the persistence library — `[Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs:25](Poly/DomainModeling/Libraries/Storage/PersistenceEmitLibrary.cs#L25)`; sqlserver/sqlite/mysql packs do the same — `[src/Poly.Packs.SqlServer/SqlServerPack.cs:25](src/Poly.Packs.SqlServer/SqlServerPack.cs#L25)`, `[src/Poly.Packs.Sqlite/SqlitePack.cs:26](src/Poly.Packs.Sqlite/SqlitePack.cs#L26)`, `[src/Poly.Packs.MySql/MySqlPack.cs:24](src/Poly.Packs.MySql/MySqlPack.cs#L24)`.
- Compiler is invoked in the lower pipeline — `[Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:174-183](Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs#L174)`.
- Threading of contributed parameters (the `db` parameter) — `[Poly/DomainModeling/Compile/ContributedMethods.cs:90-135, 258-288, 397-411](Poly/DomainModeling/Compile/ContributedMethods.cs#L90)`.
- Fallback `EnsureUnique` member when no storage port is present — `[Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:1007-1063](Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs#L1007)`.

**Runtime**
- Port surface — `[Poly.Runtime/IStoragePort.cs:10-18](Poly.Runtime/IStoragePort.cs#L10)`.
- Session key set + journal — `[Poly/DomainModeling/Runtime/ModuleStoragePort.cs:8-41](Poly/DomainModeling/Runtime/ModuleStoragePort.cs#L8)`.
- Create/Invoke host and transaction — `[Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs:120-164, 276-304, 306-383](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs#L120)`.
- Unique check on rejected create-in values — `[Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Coercion.cs:98-129](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Coercion.cs#L98)`.
- Link/unlink (no unique participation) — `[Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs:22-86](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs#L22)`.

**Emission**
- EF `Exists`/`Record`/`Release` + pending key — `[src/Poly.DslCompiler/DbContextGenerator.cs:142-238](src/Poly.DslCompiler/DbContextGenerator.cs#L142)`.
- EF unique index per unique column — `[src/Poly.DslCompiler/DbContextGenerator.cs:277-302](src/Poly.DslCompiler/DbContextGenerator.cs#L277)`.
- Minimal API 409 mapping — `[src/Poly.DslCompiler/MinimalApiGenerator.cs:1156-1180](src/Poly.DslCompiler/MinimalApiGenerator.cs#L1156)`.
- `RequiresStoragePort` — `[Poly/DomainModeling/Compile/CatalogIndex.cs:65-86](Poly/DomainModeling/Compile/CatalogIndex.cs#L65)`; host wiring at `[src/Poly.DslCompiler/MinimalApiGenerator.cs:296-306](src/Poly.DslCompiler/MinimalApiGenerator.cs#L296)`.
- JSON Schema `x-poly-unique` — `[Poly/DomainModeling/Queries/JsonSchemaExporter.cs:207-210](Poly/DomainModeling/Queries/JsonSchemaExporter.cs#L207)`.
- SHACL `x-poly-unique` — `[Poly/DomainModeling/Queries/ShaclExporter.cs:236-238](Poly/DomainModeling/Queries/ShaclExporter.cs#L236)`.
- `.http` example values — `[src/Poly.DslCompiler/HttpFileGenerator.cs:201-205](src/Poly.DslCompiler/HttpFileGenerator.cs#L201)`.
- Guide text describing the shipped behavior — `[Poly.Mcp/Docs/poly-dsl-guide.md:1016-1027](Poly.Mcp/Docs/poly-dsl-guide.md#L1016)` and `:305`, `:1195`.

### Inferences
- The `unique` path is already *layered the way a scoped unique needs*: one compiler produces AST, one port abstracts the check, one metadata record names the natural key. The missing piece is not a new mechanism but a new **key dimension** plus a way to compute its value at each create site.
- Because `UniqueConstraint` carries no data, nothing downstream can distinguish "unique globally" from "unique within X" without either (a) a new field/record, or (b) a side channel (a property, a pseudo-property like `Plate_in_Room`, or a lowering-time instrinsic). Any of those needs the same five consumers updated.

### Gaps
- I did not audit the evolution/`apply_dsl` rollback path beyond noting `DomainChange` touches relationship properties (`[Poly/DomainModeling/Evolution/DomainChange.cs:434,729](Poly/DomainModeling/Evolution/DomainChange.cs#L434)`); whether a scoped constraint would survive `evolve` round-trips is unverified.
- I did not inspect the benchmark or demo corpora for `unique` usage beyond the grep hits.

---

## Q1. Where `unique` is defined, parsed, printed, analysed, and lowered

### Takeaway
Definition and parsing are one-line arms; printing is one string; analysis deliberately treats it as statically unprovable; lowering is fully delegated to `UniqueConstraintCompiler` through the `IConstraintCompiler`/`ConstraintAnchor` seam. `EffectAnalyzer` has no unique arm at all — it inherits "always satisfied" from `ConstraintValidation`.

### Cited Findings
- Parse arm and construction: see Q0 (`DomainResolver.cs:1494-1496`).
- The property-tail loop accepts constraints and annotations **interleaved**, then emits `AddConstraintToPropertyChange` — `[DomainResolver.cs:385-411](Poly/DomainModeling/Language/DomainResolver.cs#L385)`.
- Printer: `AppendPrintedConstraints` skips only `RequiredConstraint`; `UniqueConstraint => "unique"` — `[DomainDslPrinter.cs:860-884](Poly/DomainModeling/Language/DomainDslPrinter.cs#L860)`.
- Static analysis: `UniqueConstraint => true` in `IsSatisfiedBy`, so `EffectAnalyzer.ValidateLiteralAgainstConstraints` never reports a violation for it — `[ConstraintValidation.cs:19](Poly/DomainModeling/Analysis/ConstraintValidation.cs#L19)`, `[EffectAnalyzer.cs:1315-1324](Poly/DomainModeling/Analysis/EffectAnalyzer.cs#L1315)`.
- **Latent trap:** `ConstraintQualityAnalyzer.ValidateJointConstraintSatisfiability` groups by `c.GetType()` and merges; `ConstraintMerge.Merge` returns `null` for `(UniqueConstraint, UniqueConstraint)`, which is reported as "contradictory … constraints — unsatisfiable" — `[ConstraintQualityAnalyzer.cs:91-108](Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs#L91)`, `[ConstraintMerge.cs:13-26](Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs#L13)`. So today a property may not carry two `unique` constraints at all.
- Lowering: the port guard, the assign obligation, and the post-apply rewrite are the three halves of `UniqueConstraintCompiler` — `[UniqueConstraintCompiler.cs:28-80, 82-101, 103-146](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L28)`.
- Placement is semantic: `ConstraintAnchor.Create` + `BeforeStatements`/`AfterStatements` via `ContributedMethods.Place` (`InsertBeforeConstruction` / `InsertBeforeReturn`) — `[Poly/DomainModeling/Compile/ContributedMethods.cs:171-236](Poly/DomainModeling/Compile/ContributedMethods.cs#L171)`.
- `unique` is explicitly *not* the builtin compiler's job — `[Poly/DomainModeling/Compile/ConstraintCompilation.cs:58-65](Poly/DomainModeling/Compile/ConstraintCompilation.cs#L58)`, pinned by `[Poly.Tests/DomainModeling/Compile/BuiltinConstraintCompilerTests.cs:62-70](Poly.Tests/DomainModeling/Compile/BuiltinConstraintCompilerTests.cs#L62)`.

### Inferences
- A scoped unique can reuse the entire compiler seam (`Applies`, `Compile`, `AssignObligations`, `AfterApply`) without touching analysis if it is modelled as its own constraint record; if it is modelled as a field on `UniqueConstraint`, the group-by-type merge above becomes a false "unsatisfiable" the moment a property carries both a global and a scoped unique.
- Because analysis never proves uniqueness, no scoped variant can be "proven" either; the honest posture is unchanged ("requires instance set").

### Gaps
- Whether any authoring surface allows duplicate `unique` tokens today (and what error the user sees) was not executed; only the code path was read.

---

## Q2. How the runtime enforces uniqueness today (key, timing, paths)

### Takeaway
The key is the 3-tuple `(entity name, property name, value)` rendered as one string with `\u001f` separators, held in an in-memory set for the session and additionally queried against the generated `DbContext`. It is checked on **create** (before construction, recorded before return) and on **assign** (before mutation, with release-of-old-then-record-new), plus a host-only direct write (`SetProperty`). Link, unlink, and delete never check it. There is no delete operation in the runtime at all.

### Cited Findings
- Key shape: `entity + "\u001f" + property + "\u001f" + value` — `[ModuleStoragePort.cs:40-41](Poly/DomainModeling/Runtime/ModuleStoragePort.cs#L40)`; the EF generator builds the identical composite for pending keys — `[DbContextGenerator.cs:233-238](src/Poly.DslCompiler/DbContextGenerator.cs#L233)`.
- Semantics of the three port calls documented on the interface — `[IStoragePort.cs:3-18](Poly.Runtime/IStoragePort.cs#L3)`.
- **Create guard**: the compiler emits a static helper `ExistsWith{Property}(db, value)` → `db.Exists(entity, property, value)`, then a `BeforeStatements` guard `if (ExistsWithX(db, value)) return Failure("Unique constraint violated: …")`, and an `AfterStatements` `db.Record(entity, property, value)` — `[UniqueConstraintCompiler.cs:40-79](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L40)`.
- **Assign guard**: `EffectLoweringPass` emits `uniqueCheck = this.EnsureUnique(name, value); if (!uniqueCheck.IsSuccess) return …; destination = assignedValue` — `[EffectLoweringPass.cs:392-405](Poly/DomainModeling/Lowering/EffectLoweringPass.cs#L392)`.
- `AssignObligations` finds every method whose body assigns that unique property and requires the `db` parameter — `[UniqueConstraintCompiler.cs:82-101](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L82)`.
- `AfterApply` walks each method body and rewrites the `EnsureUnique`-then-assign shape into: `if (value != null && !Equals(this.Prop, value) && db.Exists(…)) return Failure(…); previous = this.Prop; this.Prop = value; if (previous != null && previous != value) db.Release(…); if (value != null) db.Record(…)` — `[UniqueConstraintCompiler.cs:287-322](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L287)`. Anything not in that shape **fails closed** — `:173-187, 259-285`.
- **Host direct write** duplicates the same logic with the same message — `[ModuleInstanceExecutor.cs:276-304](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs#L276)`.
- **Rollback**: the executor snapshots bags, registered ids, and the port journal mark; a failure restores all three — `[ModuleInstanceExecutor.cs:343-383](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs#L343)`, `[ModuleStoragePort.cs:27-38](Poly/DomainModeling/Runtime/ModuleStoragePort.cs#L27)`.
- **Create-in rejected-value path**: when an id/value cannot be resolved to a registered instance it is probed for a unique collision before failing — `[ModuleInstanceExecutor.Coercion.cs:94-129](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Coercion.cs#L94)`.
- **Link/unlink** mutate bags only (collection add / singular assign) and never touch `_port` — `[ModuleInstanceExecutor.Links.cs:22-86](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs#L22)`.
- No `Delete` operation exists in `Poly/DomainModeling/Runtime` or the effect set (grep over `Runtime/` and `Ontology/Effects` returns no delete).
- The generated C# context's `Exists` first checks `_pendingKeys`, then per-entity iterates unique columns and runs `dbSet.Any(e => e.Prop == value)` (or `e.Prop.ToString() == value` for non-strings) — `[DbContextGenerator.cs:155-201](src/Poly.DslCompiler/DbContextGenerator.cs#L155)`.
- The message contract is one string, matched by HTTP status mapping: `Unique constraint violated: '{property}' value is already used on another '{entity}'.` — `[UniqueConstraintCompiler.cs:189-190](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L189)`, `[MinimalApiGenerator.cs:1156-1180](src/Poly.DslCompiler/MinimalApiGenerator.cs#L1156)` (`Contains("Unique constraint")` → 409).

### Inferences
- Uniqueness scope today is *the entity type across the whole session/database*: the entity name in the key makes uniqueness per (type, property), never per aggregate or per parent. Two `Room`s under different `Hotel`s cannot both have `Number = "101"`.
- The key has a natural place for a fourth segment (`scope`), and both implementations (in-memory set, EF generator) build it from the same three arguments, so adding a scope is mechanical *provided the scope value is available where the guard runs*.
- Because assign already computes `previous`/`value`, a scoped assign does not need new state: only `Exists/Release/Record` gain the scope argument.

### Gaps
- The `DomainInstanceStore` referenced by older review docs ([`docs/agent/reviews/2026-08-31-pr44-*`](https://github.com/scoizzle/Poly/tree/067bde75f69646fdf134f7c53ffdf62b463f24fd/docs/agent/reviews)) no longer exists; it was replaced by `ModuleInstanceExecutor` + `ModuleStoragePort`. I did not trace the exact commit that removed it.
- No test exercises two entities of the same type under different parents, so the global-vs-scoped semantics are only inferable from the key, not pinned.

---

## Q3. Can a `Relationship` carry properties and constraints today, and are they enforced?

### Takeaway
The IR allows it (`Relationship.Properties` exists and is in the tree), but **the DSL cannot author it**, no analysis validates constraints on it, and neither storage nor runtime reads it. Relationship properties are dead weight today: no columns, no join table, no addressing scheme, no enforcement.

### Cited Findings
- `Relationship` carries `IReadOnlyList<Property> Properties`, plus `Stages`, `Policies`, `SourceOwnsTarget`; children include `Properties` — `[Poly/DomainModeling/Ontology/Relationship.cs:19-29](Poly/DomainModeling/Ontology/Relationship.cs#L19)`.
- Nav lines parse cardinality + `owned` + target type only, then queue `PendingNav(entity, name, target, cardinality, owned)` — `[DomainResolver.cs:1334-1365](Poly/DomainModeling/Language/DomainResolver.cs#L1334)`.
- Resolution always supplies an **empty** property list: `new AddRelationshipChange(name, source, target, nav.Cardinality, [], nav.SourceOwnsTarget)` — `[DomainResolver.cs:1404-1408](Poly/DomainModeling/Language/DomainResolver.cs#L1404)`.
- The `relationship Name { … }` block form is rejected: `N2RelationshipNotSupported()` — `[DomainResolver.cs:128-129, 200-201](Poly/DomainModeling/Language/DomainResolver.cs#L128)`, thrown at `:1005`.
- Only two consumers reference relationship properties: duplicate-name diagnostics `[Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:262](Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs#L262)` and evolution `[Poly/DomainModeling/Evolution/DomainChange.cs:434,729](Poly/DomainModeling/Evolution/DomainChange.cs#L434)`.
- Storage classification iterates **entity properties only** — `[StorageAnalyzer.cs:274-276](Poly/DomainModeling/Analysis/StorageAnalyzer.cs#L274)`; `StorageNavigation` exposes only `Source`, `PropertyName`, `IsCollection`, `InversePropertyName`, target/ownership — `[StorageModel.cs:178-196](Poly/DomainModeling/Analysis/StorageModel.cs#L178)`.
- Storage relationships expose only name/endpoints/cardinality/ownership — `[StorageModel.cs:212-224](Poly/DomainModeling/Analysis/StorageModel.cs#L212)`.
- Runtime links are bag fields named `_{nav}` (collection) / `{Nav}` (singular) — `[ModuleInstanceExecutor.Links.cs:176-198](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.Links.cs#L176)`.
- The runtime's relationship view is `RelationshipContract(Name, Source, Target, Cardinality, SourceOwnsTarget)` — no properties — `[Poly/DomainModeling/Analysis/DomainModelMetadata.cs:83-89](Poly/DomainModeling/Analysis/DomainModelMetadata.cs#L83)`.

### Inferences
- A scoped unique whose scope is "the instance reached through a singular navigation" does **not** need relationship properties: the scope is a navigation on the child (`Room` of a `Bed`), not a property on the edge. The simplest reading of `unique_within(parent)` is "unique among siblings under the same parent", i.e. the child holds the to-one nav.
- If the intended reading were "unique per edge" (e.g. seat number on an enrollment), relationship properties would first need an authoring form, storage columns, and a runtime representation — a much larger change with no precedent to copy.

### Gaps
- Whether any external (MCP `evolve`) path can inject relationship properties is unverified; `DomainChange` supports it structurally but I found no tool that passes a non-empty list.
- No test asserts that relationship properties are ignored by storage/runtime; the exclusion is implicit.

---

## Q4. How EF/`DbContext`, Minimal API, JSON Schema, and SHACL emit `unique`

### Takeaway
EF gets one `HasIndex(x => x.Prop).IsUnique()` per unique column (never a composite index), with a synthetic `int Id` primary key. The API never emits a constraint; it maps a *message prefix* to HTTP 409. JSON Schema and SHACL both drop to a boolean extension `x-poly-unique` because neither can express cross-document uniqueness.

### Cited Findings
- Shadow key + unique indexes: `b.Property<int>(shadowKey); b.HasKey(shadowKey);` then `foreach (var col in store.Columns.Where(c => c.IsUnique)) b.HasIndex(x => x.<Prop>).IsUnique()` — `[DbContextGenerator.cs:277-302](src/Poly.DslCompiler/DbContextGenerator.cs#L277)`.
- The doc comment states the intent: "A `unique` property is a unique index, not the primary key, so a second unique on the same entity is stored instead of dropped" — `[DbContextGenerator.cs:277-279](src/Poly.DslCompiler/DbContextGenerator.cs#L277)`.
- `Store.KeyProperty` drives the route key, GET-by-key, and the `Created` URI — `[src/Poly.DslCompiler/MinimalApiGenerator.cs:393, 407-429, 477-493](src/Poly.DslCompiler/MinimalApiGenerator.cs#L393)`; also seed lookups `:1282-1287`.
- 409 mapping is textual: `Contains("Unique constraint") || Contains("already has a target")` — `[MinimalApiGenerator.cs:1156-1180](src/Poly.DslCompiler/MinimalApiGenerator.cs#L1156)`.
- `.http` sample values special-case unique text properties (`"example-{prop}"`) — `[HttpFileGenerator.cs:198-205](src/Poly.DslCompiler/HttpFileGenerator.cs#L198)`.
- JSON Schema: `case UniqueConstraint: schema["x-poly-unique"] = true;` with the comment "uniqueness across documents is a store fact" — `[JsonSchemaExporter.cs:207-210](Poly/DomainModeling/Queries/JsonSchemaExporter.cs#L207)`.
- SHACL: same boolean, documented as context-defined — `[ShaclExporter.cs:236-238](Poly/DomainModeling/Queries/ShaclExporter.cs#L236)`, `[Poly.Mcp/README.md:42](Poly.Mcp/README.md#L42)`.
- Only one test pins the JSON Schema projection `[Poly.Tests/DomainModeling/Queries/JsonSchemaExporterTests.cs:134-152](Poly.Tests/DomainModeling/Queries/JsonSchemaExporterTests.cs#L134)`; I found no SHACL unique test.

### Inferences
- A scoped unique cannot be emitted as `HasIndex(...).IsUnique()` on one column; it needs a **composite** index over `(scope key, value)` — EF supports it via `HasIndex(x => new { x.ParentId, x.Prop }).IsUnique()`, but `StorageColumn.IsUnique` carries no scope, so the generator would need a new field on `StorageColumn`/`StorageEntity` and a different emission loop.
- The API's route-key and `Created` URI depend on `Store.KeyProperty`, which is the *natural key* (first unique property). A scoped-unique property must not become `KeyProperty` or the REST identity of the entity becomes wrong.
- `x-poly-unique: true` is insufficient to describe scope; the honest projection would be something like `x-poly-unique-within: "Room"`, and SHACL's analog (`x-poly-unique` is already a Poly extension) is free to follow.

### Gaps
- I did not verify how EF would express the scope column at all: the storage model emits an FK *name* (`StorageForeignKey.ChildPropertyName`) but I did not confirm the generated EF model actually declares a `{Parent}Id` property for non-aggregate singular navs. Scoped unique needs a real column, so this must be checked before estimating.

---

## Q5. Which metadata already carries structure, ownership, and navigations that a scoped key could use

### Takeaway
Everything a scope needs is already published, but scattered across four records: `EntityStructureMetadata` (natural key + required set), `AggregateModel`/`AggregateEntity` (canonical parent + parent relationship + back-ref), `RelationshipLookupMetadata` (source-scoped nav lookup), and `StorageModel` (FK name + parent key). `DomainCatalogMetadata` is the umbrella index that makes those lookups cheap.

### Cited Findings
- `EntityStructureMetadata`: `IsRoot`, `HasNaturalKey`, `KeyPropertyName`, `KeyClrType`, `RequiredPropertyNames`, `ConstructorParameters` — `[EntityStructureMetadata.cs:13-79](Poly/DomainModeling/Analysis/EntityStructureMetadata.cs#L13)`; `ConstructorParameterOrder` at `:81-88` (has `IsNavigation`, `IsBackReference`, `IsCollection`, `IsOptional`).
- `AggregateModel` / `AggregateEntity`: `AggregateParentName`, `ParentRelationshipName`, `BackReferencePropertyName`, `AggregateParent` — `[AggregateModel.cs:23-54](Poly/DomainModeling/Analysis/AggregateModel.cs#L23)`; published by `[OwnershipAggregatePass.cs:12-87](Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs#L12)` as `[OwnershipAggregateMetadata.cs:7](Poly/DomainModeling/Analysis/OwnershipAggregateMetadata.cs#L7)`.
- `DomainCatalogMetadata(Domain, Types, Relationships, Index, ActionsByEntityName)` — `[DomainCatalogMetadata.cs:9-15](Poly/DomainModeling/Analysis/DomainCatalogMetadata.cs#L9)`.
- `RelationshipLookupMetadata.BySourceEntity` + `TryGetRelationship(sourceEntityName, relationshipName)` — `[DomainModelMetadata.cs:38-67](Poly/DomainModeling/Analysis/DomainModelMetadata.cs#L38)`.
- `RelationshipContractMetadata`/`RelationshipContract` — `[DomainModelMetadata.cs:79-89](Poly/DomainModeling/Analysis/DomainModelMetadata.cs#L79)`.
- `Entity.Navigations` is the authoring truth; `Domain.Relationships` is derived — `[Poly/DomainModeling/Ontology/Entity.cs:28-33](Poly/DomainModeling/Ontology/Entity.cs#L28)`.
- `StorageForeignKey(ChildPropertyName, ParentEntityName, ParentKeyProperty)` — `[StorageModel.cs:199-203](Poly/DomainModeling/Analysis/StorageModel.cs#L199)`; built only for aggregate children at `[StorageAnalyzer.cs:393-425](Poly/DomainModeling/Analysis/StorageAnalyzer.cs#L393)`.
- `EntityStructureAnalyzer.ComputeStructure` derives the natural key, root, stage and required facts from the tree — `[EntityStructureAnalyzer.cs:38-60](Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs#L38)`.

### Inferences
- A scope named by a singular navigation can be resolved at analysis time with `RelationshipLookupMetadata.TryGetRelationship(childEntity, scopeNav)`; the parent entity's identity value is `EntityStructureMetadata.KeyPropertyName` (or the shadow `Id` when the parent has no natural key).
- `AggregateEntity.ParentRelationshipName` is *not necessarily* the nav the author writes in `unique_within(parent)` — the aggregate parent is derived from create-in topology, and a plain singular reference does not make a parent (`OwnershipAggregatePass` comment: "Plain singulars never create parents here"). So a scoped unique should resolve the scope from the **named navigation**, not from the aggregate, or the two will disagree.
- `StorageForeignKey` is currently aggregate-only, which means the FK column a scoped unique would key on may not exist for a plain singular reference — a real gap to close.

### Gaps
- I did not verify whether the generated EF model emits a navigation/FK for plain (non-owned, non-aggregate) singular relationships; `StorageForeignKey` construction suggests not, but `StorageNavigation` + collection config hints may cover part of it.

---

## Q6. Which tests already pin global unique behavior, and which need scoped variants

### Takeaway
There are ~45 unique-related tests across eight areas: lowering shape, runtime collision, rollback/no-mutation, create-in, export/session stability, EF emission, JSON Schema, and parity. They all assume a single global key; a scoped unique needs sibling tests rather than edits to these.

### Cited Findings
- **Lowering shape** (`EnsureUnique`-then-assign, guard placement): `[Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs:13-232](Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs#L13)` (11 tests), `[ConstraintAssignLoweringTests.cs:230-247](Poly.Tests/DomainModeling/Lowering/ConstraintAssignLoweringTests.cs#L230)`.
- **Runtime collision + rollback**: `[Poly.Tests/DomainModeling/Runtime/ModuleInstanceExecutorTests.cs:36-110, 259](Poly.Tests/DomainModeling/Runtime/ModuleInstanceExecutorTests.cs#L36)` (`Create_DuplicateUnique_…RegistersNothing`, `Invoke_UniqueCollision_…PortUnchanged`).
- **Create-in + create-from-default**: `[Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs:144-178](Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs#L144)`.
- **Assign-before-mutate across every effect shape** (if / else / nested-if / conditional create): `[Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:391-674](Poly.Tests/DomainModeling/ActionEntityReturnTests.cs#L391)` (~12 tests).
- **Export/session stability**: `[Poly.Tests/Mcp/UniqueSurvivesExportTests.cs:32-123](Poly.Tests/Mcp/UniqueSurvivesExportTests.cs#L32)` (5), `[Poly.Tests/Mcp/UniqueSurvivesRejectedEvolveTests.cs:23-80](Poly.Tests/Mcp/UniqueSurvivesRejectedEvolveTests.cs#L23)` (4), `[Poly.Tests/Mcp/McpSmokeTests.cs:1521](Poly.Tests/Mcp/McpSmokeTests.cs#L1521)`.
- **Simulate/print parity**: `[Poly.Tests/DomainModeling/Lowering/ParityTests.cs:182, 1030-1045](Poly.Tests/DomainModeling/Lowering/ParityTests.cs#L182)`.
- **Storage-port-only domains**: `[Poly.Tests/DomainModeling/Compile/UniqueModuleNamesOnlyStoragePortTests.cs:18-80](Poly.Tests/DomainModeling/Compile/UniqueModuleNamesOnlyStoragePortTests.cs#L18)`.
- **Contributed-compiler behavior**: `[Poly.Tests/DomainModeling/Compile/ContributedMethodTests.cs:54-306](Poly.Tests/DomainModeling/Compile/ContributedMethodTests.cs#L54)` (probe compilers, duplicate-compiler diagnostic naming `Widget.Sku (UniqueConstraint)` at `:306`).
- **Coverage ledgers that will fail on a new constraint record**: `[Poly.Tests/DomainModeling/Lowering/ConstraintWriterCoverageTests.cs:85-96](Poly.Tests/DomainModeling/Lowering/ConstraintWriterCoverageTests.cs#L85)` ("Add a sample for coverage row") and `[Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs:284-285](Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs#L284)` (`UniqueConstraint => … Methods.Any(m => m.Name == "EnsureUnique")`).
- **EF**: `[Poly.Tests/DomainModeling/Lowering/DbContextGeneratorTests.cs:187-213](Poly.Tests/DomainModeling/Lowering/DbContextGeneratorTests.cs#L187)` (`IsUnique` invocation count; "SecondaryUnique_StillEmitsColumnConfig").
- **Natural key / shadow key**: `[InfrastructureAnalyzerTests.cs:259-293](Poly.Tests/DomainModeling/Lowering/InfrastructureAnalyzerTests.cs#L259)`, `[SqlServerPackTests.cs:363](Poly.Tests/DomainModeling/Lowering/SqlServerPackTests.cs#L363)`.
- **JSON Schema**: `[JsonSchemaExporterTests.cs:134-152](Poly.Tests/DomainModeling/Queries/JsonSchemaExporterTests.cs#L134)`.
- **Dogfood**: `[Poly.Tests/DomainModeling/CrmDogfoodTests.cs:117](Poly.Tests/DomainModeling/CrmDogfoodTests.cs#L117)`, `[DomainModelingSurfaceCompletionTests.cs:144](Poly.Tests/DomainModeling/DomainModelingSurfaceCompletionTests.cs#L144)`.

### Inferences
- Most existing tests remain valid under a scoped unique only if unscoped `unique` keeps its global meaning. The cheap path is additive: scoped tests prove siblings-under-same-parent collide and different parents do not; nothing existing changes.
- The two *ledger* tests (`ConstraintWriterCoverageTests`, `EveryConceptHasTreeTests`) are the ones that will break mechanically if scoped unique becomes a new `Constraint` subtype; if it becomes a field/property on `UniqueConstraint`, they will not break but the merge trap from Q1 will bite instead.
- `UniqueModuleNamesOnlyStoragePortTests` is the guard that proves the port, not a host-specific wrapper, is the enforcement; a scoped variant must extend that file rather than invent a second path.

### Gaps
- I did not count the full suite or run it; the list comes from names and grep hits, so there may be additional indirect pins (e.g. DSL round-trip tests at `Poly.Tests/DomainModeling/Parsing/PolyDslRoundTripTests.cs:593`).

---

## Q7. The smallest seam that could carry a scope id without changing analysis or VM semantics

### Takeaway
The smallest seam is the **storage port call itself**: keep `unique` analysis-neutral, keep the guard as an ordinary `Invoke` in the lowered module (so the VM is untouched), and add a fourth `scope` argument to `Exists`/`Record`/`Release`, computed at the two places the guard already runs — inside `Create` (from a nav parameter that is already in the signature) and inside the assign rewrite (from `this.<scopeNav>`). Everything else is mechanical propagation: a scope field on the storage model, a composite key in `ModuleStoragePort`, a second comparison in the generated `DbContext.Exists`, and a composite `HasIndex(...).IsUnique()`.

### Cited Findings
- The guard is already a plain `Invoke` on `IStoragePort`, so adding an argument changes no VM semantics — `[UniqueConstraintCompiler.cs:40-79, 316-322](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L40)`.
- The port is a 3-method interface; both implementations are in-repo (`ModuleStoragePort`, generated context), so a 4-arg overload or signature change is contained — `[IStoragePort.cs:10-18](Poly.Runtime/IStoragePort.cs#L10)`, `[ModuleStoragePort.cs:12-25](Poly/DomainModeling/Runtime/ModuleStoragePort.cs#L12)`, `[DbContextGenerator.cs:155-231](src/Poly.DslCompiler/DbContextGenerator.cs#L155)`.
- The key is a single composed string, so a scope segment is a one-line change in each implementation — `[ModuleStoragePort.cs:40-41](Poly/DomainModeling/Runtime/ModuleStoragePort.cs#L40)`, `[DbContextGenerator.cs:233-238](src/Poly.DslCompiler/DbContextGenerator.cs#L233)`.
- **The scope value is already reachable at the create site**: `Create{Nav}` builds the target's arguments, and the singular self/parent nav is passed as `this` — `[DomainToCSharpExporter.Notify.cs:183-210](Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs#L183)`; the call itself is `Target.Create(createArgs)` at `:285-288`.
- At the assign site the subject is already `this`, so `this.<scopeNav>` is a legal expression in the same lowering — `[EffectLoweringPass.cs:392-405](Poly/DomainModeling/Lowering/EffectLoweringPass.cs#L392)`; `LoweringContext.Subject` documented at `[docs/interpretation/domain-execution-model.md:294](docs/interpretation/domain-execution-model.md#L294)`.
- Contributed parameters are threaded to callers automatically (`ThreadToCallers`), but the **value** must already exist under the same name at the call site — `[ContributedMethods.cs:124-133, 258-288](Poly/DomainModeling/Compile/ContributedMethods.cs#L124)`, `[IsSameBinding at :397-411](Poly/DomainModeling/Compile/ContributedMethods.cs#L397)`. This is why "just add a scope parameter" is not sufficient by itself.
- Serialisation of the scope must be textual, because the port is string-typed — `[IStoragePort.cs:12-18](Poly.Runtime/IStoragePort.cs#L12)`; the existing code already does `value.ToString()` for non-strings (`[UniqueConstraintCompiler.cs:38, 321](Poly/DomainModeling/Libraries/Storage/UniqueConstraintCompiler.cs#L38)`).
- Scope facts are already published: parent natural key via `EntityStructureMetadata.KeyPropertyName` / `KeyClrType` (`[EntityStructureMetadata.cs:17-24](Poly/DomainModeling/Analysis/EntityStructureMetadata.cs#L17)`), parent ownership path via `AggregateEntity` (`[AggregateModel.cs:32-54](Poly/DomainModeling/Analysis/AggregateModel.cs#L32)`), nav resolution via `RelationshipLookupMetadata` (`[DomainModelMetadata.cs:38-54](Poly/DomainModeling/Analysis/DomainModelMetadata.cs#L38)`), FK naming via `StorageForeignKey` (`[StorageModel.cs:199-203](Poly/DomainModeling/Analysis/StorageModel.cs#L199)`).
- `StorageColumn.IsUnique` is the single flag that both EF emission and assign lowering read — `[DbContextGenerator.cs:168, 296](src/Poly.DslCompiler/DbContextGenerator.cs#L168)`, `[EffectLoweringPass.cs:359-372](Poly/DomainModeling/Lowering/EffectLoweringPass.cs#L359)`. It must not be set for a scoped unique without a scope, or EF emits a global index and assign takes the global branch.

### Inferences
- **Recommended shape (smallest correct):** a new constraint record `ScopedUniqueConstraint(string Navigation)` rather than a field on `UniqueConstraint`. Reasons: (a) the merge trap in Q1 disappears (group-by-type separates them) if global+scoped must coexist; (b) `ConstraintCheckWriter.CoverageByConstraint` is keyed by `Type` so a new record gets an explicit owner row instead of silently inheriting `unique`; (c) provenance in duplicate-compiler diagnostics stays unambiguous (`Widget.Sku (ScopedUniqueConstraint)`).
- **The scope expression belongs in the compiler, not the analysis.** `UniqueConstraintCompiler.Compile` already receives `ConstraintCompileRequest` with `Entity`, `Property`, `StorageModel`, so it can emit `ExistsWith{Property}(db, value, scope)` where `scope = <navParam>.<ParentKey>.ToString()` because the nav is already a `Create` parameter (auto-wired `this` or a caller-supplied nav). The assign rewrite can emit `this.<Nav>.<ParentKey>.ToString()` via the `ThisReference` it already uses for the old value.
- **What must change in lockstep, minimally:**
  1. `IStoragePort` (4-arg `Exists`/`Record`/`Release`, or an optional `string scope = ""` to avoid touching unrelated call sites);
  2. `ModuleStoragePort.Key` (one more `\u001f` segment; journal unaffected);
  3. `DbContextGenerator` `Exists`/`Record`/`Release`/`PendingKey` and the unique-index loop (composite index over the scope column + value);
  4. `StorageAnalyzer`/`StorageColumn` (a `UniqueWithin`/`ScopeProperty` on the column, leaving `IsUnique` false so natural-key selection and route keys are unaffected);
  5. `UniqueConstraintCompiler` (guard + assign rewrite + `Violation` message naming the scope);
  6. DSL: `DslTokenKind`/keyword map/`ParseConstraint`/`IsConstraint`/printer/presentation/MCP `add`;
  7. ledgers: `ConstraintCheckWriter.CoverageByConstraint`, `EveryConceptHasTreeTests`, `ConstraintWriterCoverageTests.Sample`.
- **What must *not* change:** `ConstraintValidation` (still "cannot validate statically"), `EffectAnalyzer` (still no arm), the VM (still `Invoke`), and `EntityStructureAnalyzer`'s natural-key selection.
- **Cheapest thing that would satisfy the letter but not the spirit:** encode the scope into the `property` argument (`"Plate@Room"`). It needs zero interface change but breaks EF's per-column `Exists` (the generator matches `propertyParam == column.Name`), so it would silently fall through to `false` and stop enforcing. Not recommended.
- **The riskiest unknown:** whether a plain singular nav actually produces a key column in the generated EF model. `StorageForeignKey` is aggregate-only, so the composite index and the `e.<Scope> == scope` predicate may have no column to bind to until that is closed.

### Gaps
- I did not verify `DbContextGenerator`'s handling of navigation properties (whether a plain singular nav becomes a shadow FK); this determines whether scoped unique is a small change or needs storage-model work first.
- I did not check whether the MCP runtime `SetProperty` path (`[ModuleInstanceExecutor.cs:276-304](Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs#L276)`) is reachable from a tool with a parent in scope; a scoped variant needs the same scope derivation there.
- No decision record or issue text was found that defines `unique_within` semantics (per-parent vs per-aggregate-root vs per-edge), so the audit cannot say which of the three the product wants.
