# TASK optional - properties required by default; opt-in optional
Status: in review. Branch slice/optional. Lane A. Review 2. Implement mill: OpenCode (review on Grok).

## Scope
- Today `required` is only a value constraint (`Poly/DomainModeling/Ontology/Constraints/RequiredConstraint.cs:3`), and requiredness is re-decided at each consumer: create coverage (`Poly/DomainModeling/Analysis/EffectAnalyzer.cs:830-852`), C# `[Required]`/ctor (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:235-241`), storage (`Poly/DomainModeling/Analysis/StorageAnalyzer.cs:292-303`), create DTO (`src/Poly.DslCompiler/MinimalApiGenerator.cs:1126`).
- Add `optional` as the explicit opt-out: token (`Poly/DomainModeling/Language/DslTokenReader.cs:207`), constraint parse (`Poly/DomainModeling/Language/DomainResolver.cs:1416-1420`, `:1546-1550`), print (`Poly/DomainModeling/Language/DomainDslPrinter.cs:821-831`).
- Analyze publishes one requiredness fact, `EntityStructureMetadata.RequiredPropertyNames`, computed in `EntityStructureAnalyzer` (`Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:71-87`, `:108-121`); move that pass immediately before `EffectAnalyzer` in `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs:31,35` so create coverage reads the bag.
- Lower/Produce read the fact: create coverage, C# type/ctor, storage `IsRequired`, DTO `[Required]`, VM/runtime create. `default(...)`, first-stage entry assignment and the auto-wired back-reference stay exemptions.
- Out of scope: `optional` on value-type/contract members (fail closed at parse), optional members in the HTTP create DTO (already held out), and null-propagated reads/operators.

## Files
- `Poly/DomainModeling/Ontology/Constraints/OptionalConstraint.cs` (new, mirrors `RequiredConstraint.cs`)
- `Poly/DomainModeling/Ontology/Constraints/ConstraintMerge.cs`
- `Poly/DomainModeling/Language/DslTokenKind.cs`
- `Poly/DomainModeling/Language/DslTokenReader.cs`
- `Poly/DomainModeling/Language/DslGrammar.cs`
- `Poly/DomainModeling/Language/DomainResolver.cs`
- `Poly/DomainModeling/Language/DomainDslPrinter.cs`
- `Poly/DomainModeling/Analysis/ConstraintValidation.cs`
- `Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs`
- `Poly/DomainModeling/Analysis/EntityStructureMetadata.cs`
- `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs`
- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`
- `Poly/DomainModeling/Analysis/EffectAnalyzer.cs`
- `Poly/DomainModeling/Analysis/StorageAnalyzer.cs`
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs`
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs`
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs`
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs`
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`
- `Poly.Mcp/Tools/DomainTools.cs`
- `Poly.Mcp/Docs/poly-dsl-guide.md`
- `src/Poly.DslCompiler/MinimalApiGenerator.cs`
- `Poly.Tests/DomainModeling/Parsing/OptionalKeywordTests.cs` (new)
- `Poly.Tests/DomainModeling/Analysis/OptionalRequirednessTests.cs` (new)
- `Poly.Tests/DomainModeling/Lowering/OptionalPropertyLoweringTests.cs` (new)
- `Poly.Tests/DomainModeling/Analysis/PassDependencyDeclarationTests.cs`
- `Poly.Tests/DomainModeling/Analysis/ActionEntityReturnTests.cs`
- `Poly.Tests/DomainModeling/Lowering/DslCompilerSqliteOracleTests.cs`
- `Poly.Tests/DomainModeling/Compile/EmitGolden/**/*.golden` (regenerate with `POLY_UPDATE_GOLDEN=1`; inspect every hunk)
- Fixture tests the full suite names with DMEFF011 (`Poly.Tests/DomainModeling/Parsing/PolyDslRoundTripTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, `Poly.Tests/DomainModeling/Lowering/StoreBindCreateTests.cs`, `Poly.Tests/Mcp/McpSmokeTests.cs`, `Poly.Tests/Mcp/SimulateCreateDogfoodTests.cs` are the likely first ones): supply the value, or mark the property `optional` only where the fixture means optional. Never weaken an assertion.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `Name: Text` (bare) | `required` alone decides create coverage/nullability; an analysis-only `create` may omit it and the printed `Create` still has a required ctor param | Required by default: DMEFF011 when omitted at create/create-in, `string` (non-null), `[Required]`, storage `IsRequired`, DTO `[Required]` | `OptionalRequirednessTests.Create_OmittingBareProperty_ReportsDMEFF011`; `OptionalPropertyLoweringTests.Export_BareTextProperty_IsNonNullableAndRequired` |
| `Note: Text optional` | Unknown token in the property tail (parse error) | `OptionalConstraint` on the tree; omittable at create; `string?`; no `[Required]`; trailing `string? note = null`; nullable storage column | `OptionalKeywordTests.Parse_OptionalProperty_IsOptionalConstraint`; `OptionalKeywordTests.RoundTrip_OptionalProperty_PrintsOptional`; `OptionalPropertyLoweringTests.Export_OptionalTextProperty_IsNullableAndOmittable` |
| `Limit: Number optional` | Same parse error | `long?`; omitted stores `null`, not `0`; a present `0` is a value | `OptionalPropertyLoweringTests.Create_OmittedOptionalNumber_StoresNull`; `OptionalPropertyLoweringTests.Export_OptionalNumberProperty_IsNullable` |
| `Name: Text required optional` | If both parsed, two unrelated constraints | Analysis error: required is the default, optional is its opt-out | `OptionalRequirednessTests.Analyze_RequiredAndOptional_ReportsError` |
| `optional` on a `value`/contract member | Same parse error as an unknown word | Positioned parse error naming value-type members (still fail closed) | `OptionalKeywordTests.Parse_OptionalOnValueType_FailsClosed` |
| `Token: Text required default("let")` | Omittable at create because of `default(...)`; non-null type | Unchanged: default still supplies the value; requiredness fact still says required (non-null), so storage/DTO stay non-null | `OptionalRequirednessTests.Analyze_RequiredWithDefault_MayBeOmitted` |
| First-stage entry-assigned property (`StartedAt`) | Not a ctor param; body-initialized | Still not create-required and not a ctor param; requiredness fact does not turn the entry assignment into a missing-create error | `OptionalRequirednessTests.Analyze_EntryAssignedProperty_MayBeOmitted` |
| `Age: Number optional range(0, 150)` omitted at create | No optional type | Omitted passes (no range check on `null`); a supplied `0` is range-checked | `OptionalPropertyLoweringTests.Create_OmittedOptionalWithRange_NoFailure` |
| `create_instance` / VM create omitting a bare scalar | Domain-bound factory threw, but domain-null validation and some analysis paths disagreed | Both paths fail the same way; optional omission stores `null` and succeeds | `OptionalPropertyLoweringTests.Create_OmittedOptional_StoresNull`; `OptionalPropertyLoweringTests.Create_OmittedBare_Fails` |

## Done when
- `optional` is a real DSL constraint: `DslTokenKind.Optional`, `"optional"` in `DslTokenReader.WordToKind`, `ParseConstraint` returns `new OptionalConstraint()`, `IsConstraint` includes it, `DslGrammar.CanonicalText` maps it, `DomainDslPrinter.PrintConstraint` prints `optional`, and `ConstraintMerge` merges two optionals. `OptionalConstraint` is a bare `sealed record : Constraint`, exactly like `RequiredConstraint`.
- `ParseValueTypeProperties` (`DomainResolver.cs:1092-1112`) throws a positioned error for `optional` (`optional` applies to entity properties; value-type members are contract payload fields). A value-type member never silently accepts and ignores it.
- `ConstraintValidation` treats `OptionalConstraint` as satisfied by any value (including null) and `Describe` says `optional`. `ConstraintQualityAnalyzer` reports an error on a property carrying both `required` and `optional`.
- New `EntityStructureMetadata.RequiredPropertyNames` is `IReadOnlySet<string>` of every entity property without `OptionalConstraint`. `EntityStructureAnalyzer.ComputeRequiredPropertyNames(Entity)` is the only definition of the rule; `ComputeStructure` publishes it, `ComputeConstructorParameterOrder` excludes optional properties from the required ctor order (the exporter/Notify trailing list carries them), and `HasRequiredEntityRef` stops treating an optional entity-typed property as an owner reference. `EntityStructureAnalyzer` runs immediately before `EffectAnalyzer`.
- `EffectAnalyzer.ValidateRequiredInitializerCoverage` reads `RequiredPropertyNames` (`context.GetStructure(targetEntity)`, fallback `EntityStructureAnalyzer.ComputeRequiredPropertyNames` for standalone use). It reports DMEFF011 for every missing required property except one with `DefaultValueConstraint`, one in `EntryAssignedPropertyNames`, or the auto-wired back-reference; the message still contains `required property '<Name>'` and now says a property without `optional` must be supplied (or have a default).
- Printed C#: a required property is `T`, gets `[Required]`, and stays a required ctor parameter; an optional property is `T?`, gets no `[Required]`, and is a trailing `T? = null` parameter assigned directly (omitted = null, not `""`/`0`). Optional + `default(...)` keeps the default path (the default still wins) and prints a nullable property.
- `StorageAnalyzer.StorageColumn.IsRequired` is `RequiredPropertyNames.Contains(prop.Name)` (fallback helper when the analyzer runs without analysis), so `.IsRequired()`/NOT NULL follows requiredness. `MinimalApiGenerator.BuildDtoTypes` marks every string member of the create DTO `[Required]` (every member is a required ctor slot; optional members are absent, as held out).
- `DomainToCSharpExporter.TrailingDefaultedProperties` becomes `TrailingOmittableProperties` (defaulted OR optional, never entry-assigned) and every call site uses it. `EffectLoweringPass.ArgumentFor` returns `(null, omitWhenTrailing: true)` for an omitted optional with no default, so create / create-in / typed create pass `null` and `KeepThroughLastWritten` can drop it; `AddCreateNavMethod` exposes the same trailing optional params.
- `DomainEntityInstance.Create`: an omitted optional is stored as `null`; the domain-bound factory presence checks and the domain-null `ValidateConstraints` both reject a missing required property (after the same default/entry-assigned exemptions). `FactorySlots` includes the trailing omittable list, so VM create and print agree.
- The DSL guide is updated in the same change: `optional` in the constraint table, the "What makes a property optional at create" section rewritten to "required is the default; `optional` is the opt-out; `default(...)` still supplies a value", the storage/transport tables attribute requiredness to "not `optional`", and value-type members named as out of scope.
- Test-first: `OptionalRequirednessTests.Create_OmittingBareProperty_ReportsDMEFF011` is written and run red (it fails on today's analyzer, which only checks `required`), and the red command/output is recorded in the implementer's Log row. Goldens change only where `[Required]`/`.IsRequired()`/nullability follows requiredness; no golden hunk adds or removes an unrelated member.
- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` is clean and the full suite passes.

## SHIP if / NOT SHIP if
SHIP if the red test is recorded, `optional` is one constraint on the syntax tree with parse/print/round-trip, `EntityStructureMetadata.RequiredPropertyNames` is the only requiredness fact and Analyze publishes it, a bare property is required at create and non-null in C#/storage/DTO, an `optional` property is omittable and nullable, create/create-in/create_instance/VM agree on the omitted value (`null`, not a type default), the guide matches the code, and the full suite is green.

NOT SHIP if `required` is repurposed as the create opt-in, a Lower/Produce consumer re-scans constraints or `OptionalConstraint` to decide requiredness, `optional` members print non-nullable or get `""`/`0` when omitted, required and optional are both accepted on one property, an optional value-type member is silently accepted, the HTTP create DTO gains a half-implemented optional field, a test is deleted/weakened, or a golden diff hides a non-requiredness change.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OptionalKeywordTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OptionalRequirednessTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OptionalPropertyLoweringTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/PassDependencyDeclarationTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DslCompilerSqliteOracleTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/UnifiedAddTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'`
`dotnet run --project Poly.Tests/Poly.Tests.csproj` (full suite)

## Hand-edit
Each touched site is one switch arm, one set lookup, or one already-named list; the new `OptionalConstraint` mirrors `RequiredConstraint`, and the requiredness rule lives in one static helper, so Scot can open any touched file cold and see what changed.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-25 | planner | opencode/deepseek-v4.1-flash | 6501436d | planned | issue #324; deps #309/#325 and #310/#326 (`6501436d`) merged; decisions #14/#15/#16 accepted |
| 2026-10-25 | implementer | opencode/opencode-go/deepseek-v4.1-flash | f95b7c2e | pushed | tests 4594/4594; red recorded (DMEFF011, pre-fact analyzer); sweep: old names gone, `OptionalConstraint` only at the fact seam, goldens requiredness-only |
| 2026-10-27 | implementer | grok/grok-4.7 | 12f5286e | requiredness adoption | red: bare Text assign of "" succeeded; bare length skipped empty; create of a bare property did not warn; action parameter IsRequired was false. Assign, length, RequiredPropertiesPass, fixed-point, and BehaviorMetadata now use IsRequired (no optional). |
| 2026-10-28 | implementer | grok/grok-4.7 | 7c9c96b8 | drop the required word | red: Parse_RequiredWord_IsRejected accepted the word and the printer emitted it. Parse rejects the word; the printer omits RequiredConstraint. Action DTO [Required] follows RequiredPropertyNames. Suite 4790/4790. Benchmarks 0 warnings. |
