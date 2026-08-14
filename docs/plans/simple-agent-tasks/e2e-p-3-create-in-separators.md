# e2e-p-3 — Create-in initializer separators

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** p-2  
**Fleet:** P4-3  

## Objective

Multi-initializer `create in Rel { A: x B: y }` prints with **whitespace** separators. Parser rejects commas.

## Exact steps

1. Failing test: print a CreateEntityInRelationship with two PropertyBindings → apply_dsl/parse succeeds. Name: `Print_CreateInMultiInitializer_WhitespaceSeparators`.
2. Change only the create-in / initializer list printer.

## Verification

- [ ] Round-trip green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainDslPrinter.cs` (create-in / binding list) | Effect IR |
| tests | |

## Status

**Status:** Not Started  
**Claimed by:**  
