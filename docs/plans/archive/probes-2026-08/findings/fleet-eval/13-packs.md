# Fleet-eval findings — Packs & input context (2026-08-12)

Slice: `src/Poly.Packs.Sqlite/`, `src/Poly.Packs.SqlServer/`, `Poly/DomainModeling/DomainInputBuilder.cs` + `DomainInputSet.cs`, `Lowering/TypeMappingRegistry` + `IStorageConvention`, pack storage-convention configuration.

Probes: `probes/fleet-eval/13-packs/library.poly`, `warehouse.poly`, `booking.poly` — all pass the 0/0 gate in `--mode entities` (generic). Pack behavior is only exercised in `--mode db`/`--mode all`, so findings below come from generating and statically reviewing DbContext output per pack (`--dbms generic|sqlite|sqlserver`).

---

## F1 — Enum-typed columns emit `.HasColumnType("<EnumName>")`; every pack produces a DbContext that dies at DB creation
- **Signal:** export/runtime divergence
- **Severity:** 🟠
- **Slice:** packs & input context (product: packs produce a working DbContext)
- **Repro:** `probes/fleet-eval/13-packs/warehouse.poly` → `dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- --mode db --dbms sqlite probes/fleet-eval/13-packs/warehouse.poly <out>` (same for `--dbms sqlserver` and `generic`). Generated `WarehouseDbContext.cs` line: `b.Property(x => x.Sku).HasColumnName("sku").HasColumnType("Sku");`
- **Expected:** enum-typed property `Sku: Sku` maps to a provider-valid store type (`INTEGER` in SQLite, `int` in SQL Server) or no `HasColumnType` (EF's default enum→int mapping).
- **Actual:** `StorageAnalyzer.ClassifyProperties` sets `baseColumnType = prop.Type.TypeName` for enums and `DbContextGenerator.BuildColumnConfigNode` emits `.HasColumnType(col.ColumnType)` unconditionally → `HasColumnType("Sku")` / `HasColumnType("Genre")` / `HasColumnType("Confirmation")`. `"Sku"` is not a known store type in any provider, so `EnsureCreatedAsync`/`Migrate` throws at runtime (unknown store type). The 0/0 compile gate passes. Reproduced in all three packs; the mechanism is the core registry/emitter but it is the pack surface's job to produce a working DbContext.
- **Proposed patch:** in `StorageAnalyzer.ClassifyProperties`, for enum columns set the SQL column type via the pack type map (e.g. `INTEGER`/`int`) or leave `ColumnType = null` and have `DbContextGenerator` skip `HasColumnType` for `IsEnum` columns.

## F2 — SqlServer pack maps `Text` → `nvarchar(max)`; natural-key / unique text columns become invalid SQL Server PK/index columns, and `length` max is silently dropped from DDL
- **Signal:** export/runtime divergence (key/index case) + silent gap (length)
- **Severity:** 🟠
- **Slice:** packs (SqlServerDefaults type mapping soundness)
- **Repro:** `probes/fleet-eval/13-packs/library.poly` → `--mode db --dbms sqlserver`. Generated `LibraryDbContext.cs`: `b.HasKey(x => x.Email); b.Property(x => x.Email).HasColumnName("EMAIL_ADDR").HasColumnType("nvarchar(max)");`. `booking.poly` → `b.HasKey(x => x.Code); ... .HasColumnType("nvarchar(max)")` (Code is `Text unique`).
- **Expected:** a text natural key / unique text column must be a keyable SqlServer type (`nvarchar(450)`/`nvarchar(n)`); a `Text length(2,200)` column should carry `nvarchar(200)` in DDL.
- **Actual:** `nvarchar(max)` is invalid as a key/index column type in SQL Server ("invalid for use as a key column in an index") → `EnsureCreated` fails at runtime. Where the column is not a key, the explicit `HasColumnType("nvarchar(max)")` overrides `HasMaxLength(200)`, so the declared `length` upper bound never reaches the schema (only the CLR `Create` enforces it). The 0/0 gate passes; nothing warns.
- **Proposed patch:** `SqlServerDefaults` should key the `Text` mapping off the column's constraint set — e.g. map `Text` → `nvarchar(450)` when `unique`/key, and `nvarchar(max)` only when unconstrained (or omit `HasColumnType` and let EF Core's own convention pick `nvarchar(450)` for keys).

## F3 — Column names are interpolated raw into CHECK-constraint SQL and constraint names; `--` / reserved words / duplicates emit DbContexts that compile but fail at DB creation
- **Signal:** silent gap → runtime divergence (fail-loud at DB creation only)
- **Severity:** 🟠 (reserved-word/duplicate), 🟡 (self-injection)
- **Slice:** packs & input context (conventions are the only validation layer; SqlServer pack is the only validator)
- **Repro:**
  - Reserved word: `Qty: Number range(0, 100) column("order")` → `--mode db --dbms sqlite` emits `t.HasCheckConstraint("CK_order", "order >= 0 AND order <= 100")` → invalid SQL (reserved word), `EnsureCreated` fails.
  - Comment injection: `Qty: Number range(0, 100) column("qty >= 0 OR 1=1 --")` → emits `HasCheckConstraint("CK_qty >= 0 OR 1=1 --", "qty >= 0 OR 1=1 -- >= 0 AND qty >= 0 OR 1=1 -- <= 100")` — the `--` comment silently trivially-true-ifies the range CHECK.
  - Duplicate: `A: Number range(0,100) column("x") B: Number range(0,100) column("x")` → two `HasCheckConstraint("CK_x", ...)` → duplicate constraint at runtime.
- **Expected:** a CHECK emitted from an analysis-verified range must be valid SQL regardless of the physical column name; constraint names must be unique per table; column/table names should be validated or quoted (provider-appropriately).
- **Actual:** `DbContextGenerator.CheckSql` and the `CK_{ColumnName}` derivation use the raw, unquoted, unescaped column name; `StorageAnalyzer` never dedupes column names. C# string escaping in `HasColumnName` is sound (no C# injection), but the SQL layer is not. The 0/0 gate passes for all cases.
- **Proposed patch:** quote/escape column names in CHECK SQL per provider (or refuse identifiers containing non-`[A-Za-z0-9_]` characters), and derive/dedupe constraint names (e.g. `CK_{table}_{column}` with a uniqueness check).

## F4 — SqlServer pack leaves `Duration/TimeSpan` (and `Decimal`, `Date`, `Time`) to generic defaults; `TimeSpan` → `interval`, which is not a SQL Server type
- **Signal:** silent gap (latent)
- **Severity:** 🟡
- **Slice:** packs (SqlServerDefaults completeness)
- **Repro:** registry-level: `new TypeMappingRegistry()` + `SqlServerDefaults.ApplyTypeMaps(r)` → `r.ToSqlColumnType("Duration")` returns `"interval"` (core generic default, Postgres-flavored). Not directly reachable via DSL today (the DSL only authors `Text`/`Number`/`Boolean`/`DateTime`/`Date` primitives), but reachable through the public registry/pack API and any future primitive.
- **Expected:** a SqlServer pack should map every key it documents (`Date`→`date`, `Time`→`time`, `Duration`→`time`, `Decimal`→`decimal(18,2)`) or fail loudly on unmapped keys.
- **Actual:** `SqlServerDefaults` overrides only Text/String/Boolean/Bool/Int32/DateTime/Timestamp/Binary/Float/Double/Guid/Uuid; `Duration`/`TimeSpan` falls through to generic `interval` — an invalid SQL Server column type.
- **Proposed patch:** add SqlServer overrides for `Date`/`DateOnly` (`date`), `Time`/`TimeOnly` (`time`), `Duration`/`TimeSpan` (`time`), `Decimal` (`decimal(18,2)`), and `Number`/`Int`/`Int64` explicitly (`bigint`).

## F5 — Convention ordering: SqlServer 128-char fail-closed validates the baseline only; a later-registered convention that lengthens names silently bypasses the guarantee
- **Signal:** fail-loud-but-sharp (fail-closed claim is order-dependent)
- **Severity:** 🟡
- **Slice:** packs (SqlServerIdentifierConvention + IStorageConvention composition)
- **Repro:** code reading of `StorageAnalyzer.BuildStorageEntity`/`ClassifyProperties` (conventions run in registration order; `SqlServerIdentifierConvention` is appended by `AddSqlServerDefaults()` at call time). `CreateWithSqlPack().AddSqlServerDefaults().AddStorageConvention(new PrefixConvention("veryLongPrefix..."))` (the documented composition pattern, cf. `SqlServerPackTests.SqlServerDefaults_WithPrefixConvention_AppliesBoth`) → the prefix is applied after validation, so a column name that exceeds 128 chars only after prefixing is never re-checked and no exception is thrown.
- **Expected:** the pack's fail-closed claim holds under composition — either re-validate after each projection or document that the convention only guards the pack's own projection.
- **Actual:** oversized names introduced by later conventions pass silently; the DbContext then fails at DB creation (or produces over-long identifiers).
- **Proposed patch:** run `SqlServerIdentifierConvention` last (e.g. document/add a finalize ordering) or have `AddSqlServerDefaults` register it after user conventions via an explicit `Build()`-time hook; alternatively validate in `DbContextGenerator` as a final gate.

## F6 — `TypeMappingRegistry` fallback is fail-open and case-inconsistent
- **Signal:** silent gap (latent)
- **Severity:** 🟡
- **Slice:** input context (TypeMappingRegistry)
- **Repro:** `new TypeMappingRegistry().ToSqlColumnType("Bogus")` → `"varchar"` (core `_ => "varchar"` fallback); `ToSqlColumnType("text")` (lowercase) → `"varchar"` in generic but `"TEXT"` under `SqliteDefaults` because the override dictionary is `OrdinalIgnoreCase` while the core switch is case-sensitive. Not reachable via the DSL today (primitive names are parser-constrained), but the registry is the public pack-authoring seam.
- **Expected:** unknown domain types should throw (fail-closed), or the fallback should be documented; case handling should be uniform across the override and core layers.
- **Actual:** unknown/mis-cased keys silently produce generic `varchar`/CLR passthrough.
- **Proposed patch:** make `DomainTypeMapping.ToSqlColumnType` case-insensitive and throw `KeyNotFoundException`-style on unknown keys, or keep a documented explicit "unknown" default.

---

## Verified-sound (not findings)
- `SqlServerIdentifierConvention` fails closed for 129-char `table("…")` and `column("…")` names through the CLI (`--mode db --dbms sqlserver`), and accepts exactly-128-char names. SQLite/generic correctly have no such convention.
- SQLite pack mappings (Text→TEXT, Number/Boolean→INTEGER, DateTime/Date→TEXT, Float→REAL, Binary→BLOB) match EF Core SQLite provider affinities and the pack doc-comments.
- Explicit `column("NAME", "TYPE")` type overrides are preserved under every pack (P4.3), including `VARCHAR2(20)`.
- C# string escaping of column/table names in `HasColumnName`/`ToTable` is sound (no C# injection from annotation strings).
- `DomainInputBuilder`/`DomainInputSet` cloning is sound: `BuildAnalysisInputs` clones `TypeMaps`; static singletons can't be mutated through consumers.
- Provider wiring in `--mode all` is pack-correct: sqlite→`UseSqlite("Data Source=…")`, sqlserver→`UseSqlServer(...)`, generic→`UseInMemoryDatabase` + `EnsureCreatedAsync`.
