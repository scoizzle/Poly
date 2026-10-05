# Followups: PR 98 (T1) Final Boss, tip 1b79fdb7

Findings numbered from F320.

## For 100x, one fix pass (blocks SHIP)
- [ ] **F320 (gate)** `Poly.Tests/TestHelpers/ValidDomain.cs` class doc: replace "Hand-built Domain that analyzes cleanly" and the sentences after it with true text (it declares the canonical primitives plus core-extension primitive seeds; it does not validate; callers may pass invalid content on purpose). Fold in F322 (a non-Core extension id is recorded but adds no primitives, as `AddDomainExtensionChange` does) and F323 (applies to `new Domain(..)` / `DomainTestFactory`; unlike `DomainFactory.Create`, extensions default to none). Keep the class name.
- [ ] **F321 (recommended, same pass)** New `ValidDomainTests`: the five canonical property types analyze clean; `Date` + `extensions: [ExtensionCatalog.TemporalId]` analyzes clean; `Date` without the extension still reports "unknown type 'Date'". Must go red if the fixture drops `Text`.
- [ ] Do **not** remove `.Where(ExtensionCatalog.Core.Contains)`. It mirrors product behaviour (`DomainChange.cs` AddDomainExtensionChange.ApplyTo ~685, `PolyDslParser.cs:154-158`).

## For Foreman (plan/docs, at or after merge)
- [ ] Record the 10 waived probe failures with owners in the plan (they currently live only in the PR body and the ruling):
  - G1: `CreateEntityInstance_WithRelationshipName_WrongSource_FailsLoud`, `CreateEntityInRelationship_WrongSource_FailsLoud`, `EvaluatePolicy_PathPrefix_MultipleLinkedTargets_Throws`, `ParityTests.Create_WhenDefaultViolatesEquality_FailsWithTheSameMessage` (assert the gate's message once G1 defines it).
  - Redesign: `OnEntryEffect_Throws_StageStillSet_NotifyStillFires` (needs a valid way to make OnEntry throw).
  - Known gap: `EvaluatePolicy_SelfRelationship_OutboundOnly_TargetSeesNoReports` (self-relationship quantifier unsupported).
  - N6: `InvokeAction_StringConcat_Assign_Concatenates` (analyzer rejects Text + Text; Scot 2026-10-04: concatenation is valid).
  - G3: `OracleToolTests.OracleExpression_And_Works`, `_AgeGte_PassesForAdult`, `_AgeGte_FailsForMinor`.
- [ ] **F325** T1 card Files line: OracleToolTests is G3's; the real set is 11 test files including ParityTests, plus the fixture.
- [ ] G1 done-when should name these 10 so "0 failures" is measured against the same set.
- [ ] F324 (no action): the `someRel` NoStore test still says what it does.
