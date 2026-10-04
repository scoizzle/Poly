# PR 91 (slice C4a) Final Boss follow-ups: 2026-10-04

- **SHA**: `a868496266f03eaaa78f9dfacf9185f2f88808d9`
- **Review**: `docs/agent/reviews/2026-10-04-pr91-a8684962-final-boss.md`
- **Verdict**: SHIP (0 bugs, 5 suggestions, 5 nits). Hand-editability gate: YES. Not on the V11 list: Scot hand-merges.
- **Mill**: OpenCode `opencode-go/deepseek-v4.1-flash`, no fallback.

## Open

None of these block the merge. The first group is worth doing in this PR (cheap); the rest can ride with the slice that owns them.

- [ ] **F175** (suggestion) `Poly.Tests/DomainModeling/Lowering/ParityTests.cs:217-240`: add rows so mutants survive no more: expected 5.5 vs actual 5 (reject), `"Active "` with trailing space (reject), `I` vs `ı` (reject), null actual on a Text property (reject).
- [ ] **F170** (suggestion) `ParityTests.cs:215-216`, `DomainEntityInstance.cs:176` vs `Notify.cs:339`: first-violation message differs when two properties are violated (declaration order vs `OrderBy(Name)`). Add a `KnownGap_` row owned by C2b, or reword the comment to "single violation".
- [ ] **F171** (suggestion) `DomainEntityInstance.cs:209-214`: printed skips a null `ExpectedValue`; the simulator rejects. One line: `case EqualityConstraint { ExpectedValue: not null } eq:`, plus a row.
- [ ] **F172** (suggestion) `DomainEntityInstance.cs:209`: `Convert.ToDecimal` throws `OverflowException` for an expected double beyond decimal range (1e30, NaN, infinity); printed rejects with a message. Guard it and add a row (or fold into C2b, which deletes this code).
- [ ] **F173** (suggestion) `ConstraintQualityAnalyzer.cs`, `CSharpGenerator.WriteConstant`: an equality expected value whose type does not match the property (or Guid/DateOnly/DateTime/enum) passes Analyze and the simulator, but the printed code does not compile. Reject in Analyze or scope equality to Text/Number/Bool; give it an owner in the plan.
- [ ] **F174** (suggestion) `EffectLoweringPass.cs:259-263, 321-418`, plan C4c card: equality is not enforced on action `assign`, entry `assign`, or `SetProperty` on either side (decision 12). Add it to the C4c scope and the T3 matrix; not a parity row (the sides agree).
- [ ] **F176** (nit) `ParityTests.cs:199`: `Types = [entity]` drops non-entity types; use the `ReferenceEquals` replace idiom from `MinimalApiGeneratorTests.cs:249-257`.
- [ ] **F177** (nit) `ParityTests.cs:213-216`: name `KnownGap_CreateOutOfRange_SimulateThrowsAndPrintedReturnsFailure` instead of the glob, and note `CreateThrowsVersusFails` changes with C1b.
- [ ] **F178** (nit) `Poly.Tests/TestHelpers/ParityScenario.cs:30-37`: DSL path analyzes twice; `FromDomain` does not check `HasErrors`.
- [ ] **F179** (nit, pre-existing, separate PR) `CSharpGenerator.cs:1276-1295`: double/decimal constants print with the current culture (de_DE prints `5,5`).
- [ ] **F180** (nit, pre-existing, separate follow-up) five tests fail under tr_TR on master too: `Export_RangeNegativeAndFractionalBounds_Parse`, `OpenRange_VerifiedEnvelope_KeepsBoundOpen`, `MixedTypes_IntAndDouble_PromotesToDouble`, `MixedTypes_ComplexExpression_PromotesCorrectly`, `DecimalNumbers_Addition_ReturnsDouble`.

## Closed this SHA (no action)

- Plan C4a SHIP condition: parity row red on master (5 rows red), green on the PR (35/35).
- Twin tag: names C2b and says what to delete; C2b card reciprocates.
- Diff scope: `DomainEntityInstance.cs` plus two test files; no `.golden`; golden regeneration clean; `git diff --check` clean.
- Entry-assigned known gap: real, owner C2b correct, turns red when fixed (M15).
- Mutants killed: remove case, invert, case-insensitive, no numeric branch, message edits, skip defaults, return null, entry-assigned fix.
