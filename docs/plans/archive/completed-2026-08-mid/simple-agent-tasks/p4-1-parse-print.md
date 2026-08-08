# P4-1 — Parse and print `when any|all`

**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06

## Implementation notes

- `PolyDslParser`: new `ParseSubscriptionQuantifier()` helper (identifier text match
  `any`/`all`, OrdinalIgnoreCase — mirrors the `invoke any|all` pattern); called in
  both `ParseSubscription` (stage-level) and `ParseEntitySubscription` (entity-level)
  right after consuming `when`. Omitted quantifier → `Each`.
- `DomainDslPrinter.PrintSubscription`: emits `any`/`all` keyword only when
  `Quantifier != Each` (both stage and entity-level go through this one method).
- Tests (`PolyDslRoundTripTests`): 4 new — `Parse_WhenAny_RoundTrips` (Any + `as`
  binder), `Parse_WhenAll_MultiStage_RoundTrips` (All + multi-stage), `Parse_When_
  OmittedQuantifier_IsEach_AndPrintOmitsKeyword` (default stays Each, printer omits
  keyword), `Parse_EntityLevelWhenAny_RoundTrips` (entity-level Any). Each asserts
  parse → print → reparse structural identity.
- Verified: 1849/1849 green after this task (4 new tests).
Tokenizer/parser accept optional `any`/`all` before relationship name on `when`; stamp `StageSubscriptionQuantifier` correctly; printer emits same form. Default omit → Each.

## Required reading

- `PolyDslParser` subscription/`when` path  
- `DomainDslPrinter` when emission  
- Invoke any/all parse for pattern  

## Exact steps

1. Parse `when any Rel Stage`, `when all Rel Stage`, multi-stage list, optional `as name`.  
2. Reject bad tokens honestly.  
3. Printer: emit `any`/`all` only when not Each.  
4. Round-trip tests (parse → print → parse structural).  

## Verification

- [ ] Round-trip tests green  
- [ ] Each-without-keyword still Each  

## File ownership

- **Edit:** Parsing (`PolyDslParser`, tokenizer if needed, `DomainDslPrinter`) + parse/print tests  
- **Do not edit:** DomainInstanceStore notify, guide (P4-4)  

## Status

**Status:** Not Started  
