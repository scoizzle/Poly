# COH-0 — Design locks and file ownership

**Difficulty:** S  
**Status:** `[ ]`

## Objective

Lock parallel chain ownership and move strategy before any agent edits production files.

## Exact steps

1. Confirm Option C incremental path: **Runtime/ first**; no Parsing→Dsl this suite.  
2. Record primary files per chain in progress notes:

| Chain | Primary production files |
|-------|--------------------------|
| R | `DomainEntityInstance.cs`, `DomainInstanceStore.cs`, `InvocationResult.cs` → `Runtime/` |
| D | Expression rewrite methods in DomainEntityInstance (or extracted helper) + `DomainExpressionDispatch.cs` |
| E | `EffectAnalyzer.cs` (+ EffectDispatch) — coordinate if R moves EntityInstance |
| V | `DomainChange.cs`, `DomainMutationContext.cs` |

3. If R and D both touch DomainEntityInstance: **serialize R before D**, or D works only after R move with updated paths.  
4. Mark Done — no production code.

## Verification

- [ ] Ownership table in notes  
- [ ] R-before-D rule explicit  

## Status

**Status:** Not Started  
