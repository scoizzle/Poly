# Micro-Task: MatchNumeric positive control + shared helper

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6f**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [ ] Not Started  
**Depends on**: `ws8-spike-harden-negative-subject-tests.md` (Done)

## Objective

Prove `MatchNumeric` is **sharp in both directions**: true when a working subject returns the value; false for Dict/Expando. Avoid a helper that only ever sees “false.”

## Code review finding

`MatchNumeric` is only used to assert **false** on Dict/Expando. There is no test that `MatchNumeric(result, 99999)` is **true** when Property("Age") is correctly read from a sealed record.

## Exact Steps

1. Add test on `PersonRecord` or `StrictBag` with `Age = 99999`, lower `Property("Age")`, execute VM.
2. Assert `MatchNumeric(result.Value, 99999)` is **true** (proves helper detects correct int/long reads).
3. Optional: move `MatchNumeric` / `IsVmTrue` to a small shared test helper under `Poly.Tests/TestHelpers/` if used from multiple files.
4. Keep Dict/Expando negatives using the same helper.

## Verification

- [ ] Positive control test green
- [ ] Negatives still green
- [ ] Helper not duplicated carelessly

## Out of Scope

- Production PolicySampleSubject (#6d)
- MCP
