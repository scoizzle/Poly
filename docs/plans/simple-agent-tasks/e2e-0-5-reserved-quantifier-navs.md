# e2e-0-5 — Reserved `any`/`all`/`none`/`count` nav names

**Difficulty:** S  
**Status:** `[ ]`  
**Fleet:** P7-4  
**Prereq:** 0-1 if you only document; none if you add analysis  

## Objective

Navigations named `any`, `all`, `none`, or `count` are either **analysis-rejected** or **documented as unusable** in expression reads. Pick analysis-reject (fail-closed). Do not leave them silently unreadable.

## Exact steps

1. Failing test: entity with `any: many Foo` (or a nav named `all`) → analysis diagnostic, not later CS/eval confusion.
2. Smallest analyzer check (relationship / name pass already walking nav names). Do not special-case the expression parser beyond what exists.
3. If 0-1 already documented the reservation, keep that sentence and point at the diagnostic.

## Verification

- [ ] Named test fails closed at analysis  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| relationship / name analyzer under `Poly/DomainModeling/Analysis/` | `DslExpressionParser.cs` |
| `Poly.Tests/DomainModeling/Analysis/**` | exporter |
| one sentence in `poly-dsl-guide.md` if 0-1 missed it | rewrite guide |

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** `ReservedQuantifierNav_AnyAsRelationshipName_FailsClosed` fails closed; guide §7 updated; 2065/2065 green; build 0/0
