# e2e-p-4 — EqualityConstraint print

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** p-3  
**Fleet:** P4-4  

## Objective

`EqualityConstraint` must not print `/* equals */`. Either emit a form the parser already accepts, or stop attaching the constraint on the product path. Do **not** invent a new constraint keyword unless parse already has one.

## Exact steps

1. Find printer arm + how the constraint is constructed (evolution vs DSL).
2. Failing round-trip or failing “print is parseable” test. Name: `Print_EqualityConstraint_IsParseableOrOmitted`.
3. Smallest fix: parseable print **or** omit from print if the constraint cannot be authored (then document in one guide sentence only if e2e-0 is done — otherwise leave a note on this task).

## Verification

- [ ] No `/* equals */` in printer output  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DomainDslPrinter.cs` | new constraint type |
| tests | parser new syntax unless one already exists |

## Status

**Status:** Not Started  
**Claimed by:**  
