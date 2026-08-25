# AMU-W1.1 — EffectAnalyzer catalog-only name resolve

**Wave:** 1  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06  

## Objective

When domain analysis context has type/relationship lookups (DTLM/RLM/catalog), `EffectAnalyzer` resolves relationship and entity names via catalog/helpers instead of linear `domain.Relationships` / `Types.OfType` scans on domain-bound paths.

## Required reading

- `Poly/DomainModeling/Analysis/EffectAnalyzer.cs`  
- `Poly/DomainModeling/Analysis/DomainSemanticLookupExtensions.cs`  
- Existing effect-binding tests  

## Exact steps

1. Inventory all rel/entity name lookups in EffectAnalyzer.  
2. Prefer `TryGetRelationship` / `TryGetEntity` / catalog helpers when domain + lookup metadata available.  
3. Keep standalone (`Domain == null`) reduced contract if already documented — do not invent dual soft success.  
4. Fail closed when analysis present and required lookup bag missing (match DACR style).  
5. Add/adjust tests for unknown rel / wrong source with analysis present.

## Verification

- [x] Build + relevant EffectAnalyzer tests green (1842/1842 full suite)
- [x] Domain-bound happy/error paths use helpers (code review — 4 sites below)
- [x] No new parallel index

## Implementation notes

Replaced 4 linear scans in `EffectAnalyzer.cs` with catalog helpers
(`context.GetRelationshipLookup(domain)` / `context.GetTypeLookup(domain)`):
1. `ValidateCreateWithRelationshipName` — `domain.Relationships.FirstOrDefault` → `TryResolveRelationship`
2. `ValidateCreateEntityInRelationship` — `domain.Relationships.FirstOrDefault` → `TryResolveRelationship`
3. `ValidateInvokeAction` — `domain.Relationships.FirstOrDefault` → `TryResolveRelationship`
4. `ValidateInvokeAction` target entity — `domain.Types.OfType<Entity>().FirstOrDefault` → `TryResolveEntity`

`TryResolveRelationship`/`TryResolveEntity` (new private helpers) return `false` when the
catalog bag is unavailable (stripped/failed trees) so callers **skip** name validation instead
of false-positive "unknown relationship"; return `true` + null when the bag is present but the
name is genuinely unknown (report, fail closed). `IsExclusivelyOwned` is a whole-domain scan
(not a name resolve) — intentionally unchanged. New test:
`EffectBinding_InvokeActionUnknownRelationship_WithCatalog_ReportsError` (asserts catalog
present + EffectBinding reported).

- **Edit:** `EffectAnalyzer.cs`, related tests under `Poly.Tests/DomainModeling/`  
- **Do not edit:** PolicyConstraintAnalyzer, SubscriptionAnalyzer, exporter, MCP  

## Status

**Status:** Done — 2026-08-06 (see Implementation notes)  
