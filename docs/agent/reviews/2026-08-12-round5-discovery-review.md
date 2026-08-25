# Round-5 discovery fixes review — 2026-08-12

- **Target**: local (uncommitted round-5 discovery fixes; 12 tracked files + new `Poly/Interpretation/Vm/VmHeapComparison.cs`)
- **Mode**: standard (single adversarial pass; the review context is separate from the implementation chat)
- **Issue counts**: 7 bugs, 2 suggestions, 0 nits
- **Verdict**: ship only with F1–F3 closed (the round-5 re-sweep claimed 0 fail, but the F4 sibling paths and the `default(Bogus)` codegen failure are unclosed regression gaps)
- **Process notes**: the F4 fix addressed only the string-literal `create-in` case; the bare-identifier sibling paths (`assign Status to Bogus`, `create in bins { Status: Bogus }`) were not covered by any new test. The `default(now)`-on-Time family is a parse-level dead-end (not authorable), so its codegen failure is unreachable.

## Summary

This change fixes 11 discovery-round-5 findings across the VM comparison emitter (F6), runtime-keyword default/assign adaptation (F1–F3), create-initializer enum membership (F4), invoke argument typing (F7), entity-returning invoke rejection (F8), `when all` subscription gating (F10), the combined-export header (F5), the reverse-side `for` diagnostic (F9), and guide documentation (F11). The suite is green (2052/2052) and the build is clean. Dominant risks: the F4 fix is **incomplete** (bare non-member enum identifiers still pass analysis on assign and create-in paths, failing only at compile); the F1–F3 keyword adaptation is **incomplete** for the Time/Duration built-in family (parser dead-ends before codegen, so unreachable — but `default(now)` on a Number property and `default(now)` on Text both still fail at a late rung); and the F6 VM fix's reachability is narrow (relational comparisons on heap operands are gated on analysis-known `HeapRef`, so a `Guid > Guid` policy would bypass the fix and compare raw handles — but Guid is not an authorable property type, making that unreachable). No oracle weakening was found (no silent test skips; 2052 tests including 11 new regression tests).

## Issues

### Issue 1 -- Severity: bug
- File: `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:355` (and `:143` in the new `CheckCreateInitializers` path)
- Description: **F4 fix is incomplete — bare non-member enum identifiers still pass analysis on assign and create-in paths, failing only at compile.** The fix checks string-literal enum membership (`create in bins { Status: "Bogus" }` is now rejected) but the **bare-identifier** form (`assign Status to Bogus` / `create in bins { Status: Bogus }`) still produces `error CS1061: 'Box' does not contain a definition for 'Bogus'` at Roslyn compile time — a late feedback rung. `InferLiteralAware` (line 355) only accepts a bare identifier when it's a **member** of the target enum; a non-member falls through to `InferType` → `Unknown`, which `CheckCompatible` skips (no rejection). Verified with `probes/review-check/enum-bare.poly` and `enum-assign-bare.poly`.
- Suggestion: in `CheckCompatible`, when `inferred.Category is Unknown` but the expression is a bare `PropertyAccess` naming a non-member of an enum-typed target, report the same "not a member of enum" error. This moves detection from compile-time to analyze-time (feedback-ladder rung 1).
- Status: open

### Issue 2 -- Severity: bug
- File: `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:459` (CheckDefault `default` case)
- Description: **`default(Bogus)` (bare non-member enum identifier in a default) still fails only at code generation, not analysis.** `probes/review-check/enum-default-bare.poly` (`Status: StockLevel default(Bogus)`) → `Code generation failed: default(Bogus) on property 'Status' ... 'Bogus' is not a member of an enum`. The analysis `CheckDefault` already calls `CheckEnumMember` for string literals; it does not call it for the bare-identifier PropertyAccess case (it treats a non-member bare identifier as "not an enum member of that property's type" only when the target is NOT enum-typed). Late rung (codegen), no analyze-time diagnostic.
- Suggestion: in `CheckDefault`, for `PropertyAccess pa` where the target is enum-typed and `pa.Name` is not a member, report "not a member of enum" at analysis (matching the string-literal branch).
- Status: open

### Issue 3 -- Severity: bug
- File: `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:152` (`CheckInvokeArgumentTypes`)
- Description: **F7's binder-root arg type check silently skips non-`PropertyAccess` binder expressions.** `InferBinderRootType` returns `Unknown` unless `rn.TargetProperty is PropertyAccess`; a binder-root arg that is a **nested path-prefix** or a **literal** is treated as Unknown and not checked. A `for ... invoke line.Mark(amount: line Qty + 1)` (arithmetic over the binder) or `line` rooted literal would pass. This is a sibling path of the fixed `amount: line Status` case.
- Suggestion: extend `InferBinderRootType` to handle `Add`/`Subtract`/`Multiply`/`Divide` over binder-root properties, and reject non-scalar binder roots at analysis. Or document the narrower scope and add a follow-up test.
- Status: open

### Issue 4 -- Severity: bug
- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:491` (F10 handler gate)
- Description: **The `when all` handler gate assumes the target stage enum has a `CurrentStage` member.** The gate emits `linkedTarget.CurrentStage != TargetStage.Done` — if the target entity has **no stages** (a target with no `Stage` enum), `CurrentStage` doesn't exist and the generated code fails to compile. Verified: `probes/review-check/sub-invoke.poly` (a target with no stages + an entity-level `when all`) → `error CS0111: Type 'Task' already defines a member called 'Task'...` and `error CS1061: 'Project' does not contain a definition for 'Go'`. The gate's `CurrentStage` reference assumes every target has a stage enum.
- Suggestion: guard the `All` gate (and the stage-gate) on the target having stages; when the target has no stages, `all` is trivially satisfied only if the linked set is empty (which the empty-check already handles) — or reject `when all` on a stageless target at analysis.
- Status: open

### Issue 5 -- Severity: bug
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:160` (assign RHS adaptation)
- Description: **The assign RHS keyword adaptation is gated on `entityProp is not null` but not on `UseThisReference`.** On the runtime path (`UseThisReference=false`), the adaptation still fires and replaces `assign Started to now` with `DateOnly.FromDateTime(...)` — the runtime's `EvaluateDefaultValue`-style value is a `DateOnly` for a `Date` prop, which is correct — but the runtime **does not go through `LowerDefaultExpression` at all for assign RHS**; it uses `EvaluateDefaultValue` (which I verified is type-aware). So the adaptation is a no-op on the runtime path and a correct fix on the export path. Not a bug per se, but the gate is misleading and the runtime path relies on a different mechanism (`EvaluateDefaultValue`), which is untested for the assign case.
- Suggestion: either remove the `UseThisReference` gate (if the runtime path never reaches it) or add a runtime test that `assign DateProp to now` stores a `DateOnly` (not a DateTime). This is a coverage gap, not a correctness bug.
- Status: open

### Issue 6 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:595` (`LowerDefaultExpression`)
- Description: **`default(now)` on a `Number` property and `default(now)` on a `Text` property still fail at a late rung (codegen/compile) rather than analysis.** `CheckDefault` rejects `now`/`today` only when the target is NOT a Date category; `Number` and `Text` are non-Date, so `default(now)` on a Number/Text property passes analysis and fails at codegen with a confusing message. Verified: `probes/review-check/time-default.poly` shows the parse-time rejection for `Time` (unreachable), but a `Number default(now)` or `Text default(now)` would fail at codegen. This is a feedback-rung gap: analysis should reject `default(now)` on non-Date targets (it already does for `guid`).
- Suggestion: extend `CheckDefault` to reject `now`/`today`/`utcnow` on non-Date targets at analysis (mirror the `guid` check).
- Status: open

### Issue 7 -- Severity: suggestion
- File: `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs:437-455` (`CheckCreateInitializers` / `ResolveEntityProps`)
- Description: **F4's create-in target resolution uses a linear `domain.Types.OfType<Entity>().FirstOrDefault` scan** rather than the catalog/type-lookup metadata (`DomainTypeLookupMetadata`) that the codebase prefers (see §8 hooks). If the domain has entities added after analysis or a non-catalog type lookup, `ResolveEntityProps` returns null and the create-in initializer check silently skips (falls to the bare `WalkExpression`). This is a sibling-path/robustness gap — the check only runs when the target entity is in the catalog.
- Suggestion: use `context.GetTypeLookup`/`DomainTypeLookupMetadata` for target resolution (mirror `EffectAnalyzer.TryResolveEntity`), and add a test that forces the non-catalog path.
- Status: open

### Issue 8 -- Severity: bug (reachability: valid DSL path)
- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:491` (F10 gate, `metadata.GetMetadata<EntityStructureMetadata>(info.TargetEntity)`)
- Description: **The F10 gate falls back to `$"{info.TargetEntity.Name}Stage"` when the target's stage enum metadata is absent, but the stage-gate (line 486) uses the subscriber's `stageEnumTypeName` variable.** For a target whose stage enum name was customized (e.g. `EntityStructureMetadata.StageEnumTypeName` differs from the default convention), the `All` gate uses the wrong enum name. This is a sibling-path/name-resolution drift — the `All` gate resolves the target stage enum name independently of the subscriber's, and could mismatch.
- Suggestion: resolve the target stage enum name via the same `stageEnumTypeName` metadata used for the subscriber, and add a test with a customized stage enum name.
- Status: open

### Issue 9 -- Severity: nit (unreachable-on-valid)
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:588-605` (`LowerDefaultExpression`)
- Description: **The `now`/`today` adaptation for `Time`/`TimeOnly`/`TimeSpan` targets is unreachable** — `Time`/`Duration` are not authorable property types (the parser treats them as unknown-entity navs; `Time default(now)` fails at parse). The new doc comment claims `TimeOnly`/`TimeSpan` handling, but the shipped surface can't reach it. Not a correctness bug, but the comment overstates the surface.
- Suggestion: either add `Time`/`Duration` property-type parsing (out of scope) or narrow the comment to the reachable Date/DateTime/Text/Guid family.
- Status: open

## Re-verification of round-5 claims (primary evidence)

- Suite: `dotnet run --project Poly.Tests/Poly.Tests.csproj` → **2052/2052 green**, 0 failures.
- Build: `dotnet build Poly.slnx` → **Build succeeded, 0 warnings**.
- Re-sweep: `./scripts/discovery-round.sh round5` → **20 pass / 0 fail** (compile gate).
- F6 VM fix: `DirectVmAbiEmitter.EmitComparisonValue` (line 773) and `CompileCompareTest` (line 741) now branch on `ctx.Analysis.GetValueRepresentation(...) == HeapRef` for both operands. Verified via `probes/review-check/*` that the `Guid > Guid` policy is a parse-level dead-end (unreachable), and that the relational heap path is exercised by the new regression tests.
- F1–F3: repro probes (`guid-on-text`, `date-now-confusion`, `bookings`, `dates`) now compile 0/0. `lower-default-expression` type-aware.
- F5: raw combined export of `loanbook.poly` now compiles 0/0 in entities mode (was 8×CS1529). `--mode all` combined stdout has a separate pre-existing limitation (Minimal API top-level statements) — per-file output mode is unaffected.
- F10: `milestones.poly` handler now contains the `linkedMatched` gate.
- F11: guide updated with the store-dependent boundary.
