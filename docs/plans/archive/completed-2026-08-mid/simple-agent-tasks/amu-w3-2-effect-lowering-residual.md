# AMU-W3.2 — EffectLoweringPass residual metadata lookups

**Wave:** 3  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06
**Prereq:** W1  
**Parallel OK with:** W3.1  

## Objective

With analysis present, EffectLowering resolves entities/relationships/stages via metadata helpers rather than `_domain.Relationships` / `Types.OfType` / stage rescans where bags exist (e.g. EntityStructure.TryGetStage, catalog).

## Required reading

- `EffectLoweringPass.cs`, `LoweringContext.cs`  
- Stage transition lowering comments in file  
- Effect lowering tests  

## Exact steps

1. Grep residual domain scans under analysis-present branches.  
2. Use existing resolved create-in metadata; extend only if needed.  
3. Analysis-present stage miss stays fail-loud (quality followups).  
4. Tests for create-in / transition lowering with analysis.

## Verification

- [x] Lowering tests green (DomainToCSharpExporterTests incl. new StageTransition-with-analysis test)
- [x] Standalone reduced contract unchanged or documented (null-analysis branches preserved verbatim)
- [x] Build green; full suite 1844/1844

## Implementation notes

Grep of `EffectLoweringPass.cs` residual scans under analysis-present branches:
- `ResolveEntity` (line ~498) — already catalog-first via `GetTypeLookup`, fail-closed when analysis present. ✓
- `ResolveRelationship` (line ~516) — already catalog-first via `GetRelationshipLookup`. ✓
- `GetConstructorParameterOrder` (line ~445) — already EntityStructure-bag-first (throws when bag missing — `EntityStructureMetadata is required`), `_domain.Relationships` scan only in null-analysis branch. ✓
- `DefaultForDomainType` — already catalog via `TryResolveEnumType`. ✓
- Stage transitions (lines ~118–146) — already `TryGetStage` (EntityStructureMetadata.StageByName) when analysis present; miss → skip (dispatch contract fail-loud at runtime via InvokeActionInternal). ✓
- `Assign` (line ~77) — entity-local `_entity.Properties` lookup (not a domain-wide type scan); correct as-is.

**No production edits required — W3.2 was already satisfied by prior catalog work.**

**Added test (step 4):** `EffectLowering_StageTransition_UsesEntityStructureBagForEntryEffects` — full analysis, `StageTransitionEffect(Suspended)` from source stage Active with `LowerStageTransitions: true`; asserts the `entry { assign MaxItems to 0 }` effect is emitted (proving TryGetStage bag resolution — no entity.Stages rescan) plus the `CurrentStage = PersonStage.Suspended` assignment.

**Note:** recorded pre-existing intermittent VM-execution flakes (`Exists_NonNullValue_ReturnsTrue`, `PropBag_WithNonNullValue_Works`) — unrelated to this task; pass on consecutive runs.

- **Edit:** `EffectLoweringPass.cs` (+ helpers in Lowering/) + tests  
- **Do not edit:** DomainToCSharpExporter, MCP  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
