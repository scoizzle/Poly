# P4-1 — Parse and print `when any|all`

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P4-0  

## Objective

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
