# PR 99 (N5) at 7fec146c: follow-ups

Review: `docs/agent/reviews/2026-10-04-pr99-7fec146c-final-boss.md`. Verdict: **NOT SHIP**, gate NO.

## Must fix in N5 before re-review (blocking)

- [ ] **F300.** Make the property-name rule depend on the type, exactly as the DSL's `primitive-name` branch does (`PolyDslParser.cs:277-293`):
  - `Text`, `Number` or `Boolean` as a name is accepted only when `typeName` is a primitive keyword or a primitive the session knows;
  - use one shared helper for both the parser and MCP;
  - tests: `add property` `Text: Money` and `Text: Order` are refused, `Text: Date` is accepted, plus a parity case on a non-primitive type;
  - correct matrix row 9, the "cannot drift" claim, the `IsPropertyName` doc comment and the `add` description.
- [ ] **F301.** Fix the flaky `CreateDomainSession_WithNameTheDslRefuses_CreatesNoSession` by replacing the global count with the check below (verified: 10/10 green, still red with the check removed):
  ```csharp
  var created = McpSessionStore.ListSessions()
      .Any(id => McpSessionStore.TryGet(id, out var state) && state.Domain.Name == name);
  await Assert.That(created).IsFalse();
  ```

## Fix in the same round (wording; part of the gate)

- [ ] **F302.** Rename `UnifiedAddTests.Add_Entity_WithNameTheArtifactIdRuleRejects_Fails` and `CreateDomainSession_WithNameTheArtifactIdRuleRejects_Fails` to say "DSL", e.g. `…WithNameTheDslRefuses_Fails`.
- [ ] **F306.** PR body:
  - row 11: blank names were already refused for every kind on master;
  - re-measure the mutation counts after the F301 fix.
- [ ] **F307.** `InvalidNameMessage` and the `add` description: mention that a property may be `Text`/`Number`/`Boolean` when the type is a primitive.

## Optional in N5

- [ ] **F303.** Pin `_` at the start of a name with an absolute test: `add entity "_x"` succeeds and `_x: entity {…}` parses. The mutation that removes `_` from `IsWordStart` is green today.
- [ ] **F309.** Add `Boolean` to `Add_Property_NamedAfterAPrimitiveKeyword_Succeeds`.
- [ ] **F310** (pre-existing). The `add` `kind` parameter description lists 8 of the 13 kinds.

## For Foreman to card (do not hold N5)

- [ ] **F304** (pre-existing). `DomainDslPrinter.NeedsQuotes` is a second, weaker identifier rule. Contract `source "default"` / `version "1"` is exported unquoted and does not re-apply. Fix: `!DslTokenReader.IsIdentifier(value)`.
- [ ] **F305** (pre-existing, already being carded). The four contract `add` kinds return `Success=false` "No changes applied…" although the change is stored and the revision is incremented. Reproduced identically on master `89098229` and head `7fec146c`.
- [ ] **F308.** The plan has no N5 card or F263 entry. Add one, and mark F263 closed when N5 merges.

## Decisions recorded

- A new test that fails CI at random (F301) blocks SHIP. CI runs TUnit with default parallelism and the repo has no serialization config.
- The F263 rule is "accept exactly when the DSL accepts". For property names that includes the type condition, so F300 is a bug against N5 and not a later card.
- Names with leading or trailing whitespace are refused by MCP. The DSL never sees such a string as one name, so this is consistent and not a divergence.
- The F305 contract bug is pre-existing and recorded accurately, so it does not hold N5.

## Re-review checklist (next head)

- [ ] F300 tests and the probe matrix: property `Text`/`Number`/`Boolean` × primitive, value type, entity, enum, against the DSL.
- [ ] F301: 10+ class-alone runs green, and 3+ full-suite runs green.
- [ ] The scanner is still byte-identical if it is touched again (re-run the differential).
- [ ] `git grep IsValidPart` is empty, and `ArtifactId.cs` still has an empty diff against `6dbab628`.
