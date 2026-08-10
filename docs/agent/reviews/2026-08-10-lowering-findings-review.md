# C# export + lowering review (findings pass) — 2026-08-10

- **Target**: local — `git diff 8aa72834..HEAD` (commits `a8abbaa7` + `194f6086`: enum/nav lowering, DMEFF010/011, ESM completeness, DslCompiler fixes, F/G/I codegen quality)
- **Mode**: standard (Pass B subagent failed to start; merged external reviewer findings + own Pass A)
- **Issue counts**: 1 bug-class behavior change, 3 minor findings — **all 4 fixed + pinned**
- **Verdict**: findings confirmed with primary evidence and closed in the same change set

## Summary

The change set consolidates lowering onto shared analysis metadata (ESM) and fixes codegen quality. The external reviewer's 4 findings all verified against current source: one silent behavior change in the stage-transition required-property warning (needed documentation + a pinning test), dead code in `RequiredPropertiesPass`, duplicated `entryAssignedProps` derivation (a real drift risk), and a too-narrow enum-keyword check. All closed; suite 1961/1961.

## Issues

### Issue 1 -- Severity: bug-class (silent behavior change)
- File: `Poly/DomainModeling/Analysis/EffectAnalyzer.cs:1296`
- Description: `ValidateStageTransitionRequirements` dropped the entity-level `RequiredPropertiesMetadata` fallback (was `stageMeta ?? entityMeta`). This removed warnings for BOTH the creation-required false positives AND entity-level policy `Exists` targets (`HasSource: policy { source exists }`) — the latter were link-time invariants, not transition-assign concerns, so removing them is correct, but the change was silent and only the RequiredConstraint case was tested.
- Suggestion (done): documented the semantic precisely (creation invariants = DMEFF011/ValidateCreateEntityRequirements; entity-policy Exists = link-time; stage-scoped = genuine transition requirements) and added `EffectUnsatisfiedRequirement_EntityPolicyExists_DoesNotWarnOnTransition` so the behavior is intentional.
- Status: fixed

### Issue 2 -- Severity: minor (dead code)
- File: `Poly/DomainModeling/Analysis/RequiredPropertiesPass.cs`
- Description: `PublishStage` was a comment-only no-op (the standalone Stage visit can't resolve Exists targets without the owning entity's property map) but the `case Stage` dispatch still called it; class doc claimed "(stages when collectable)" without saying where.
- Suggestion (done): removed the Stage dispatch case + dead method; corrected the class doc (stage metadata published from the entity visit).
- Status: fixed

### Issue 3 -- Severity: minor (drift risk)
- File: `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:106` + `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:128`
- Description: `entryAssignedProps` hand-derived identically in both the analyzer (for the ctor signature) and the exporter (for ctor emission), with a comment demanding they stay in lockstep — a CS7036-class drift risk (the same class this whole series eliminated for constructor params).
- Suggestion (done): published as `EntityStructureMetadata.EntryAssignedPropertyNames` via a shared `ComputeEntryAssignedPropertyNames` helper; exporter reads the bag. Single source of truth.
- Status: fixed

### Issue 4 -- Severity: minor (incomplete fail-closed message)
- File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:940`
- Description: the enum-member reserved-word check covered only the 5 type keywords (`Text/Number/Boolean/DateTime/Date`); lowercase structural keywords (`create`, `in`, `stage`, `entry`) lexed as keyword tokens and hit the cryptic `Expected RBrace, got 'create'`. Fails closed, so not a correctness bug — but the message the change aimed to fix was still cryptic for those.
- Suggestion (done): widened to any letter-led non-terminator token (covers all 43 keyword words via the tokenizer's case-sensitive `WordToKind`); tests pin the error for `Number` and `create`, the capitalized-forms-are-valid case, and trailing-comma/valid-member non-regressions.
- Status: fixed

## Process notes

- The keyword check reality: `WordToKind` is **case-sensitive** (`"create"` → keyword, `"Create"` → Identifier). The reviewer's examples (Entry/Create/In/Stage capitalized) are actually *valid* enum members; only lowercase keyword spellings collide. Worth remembering for future DSL work.
- The entity-required→transition-required conflation was a genuine analyzer-correctness gap (the "5 false warnings" from TinyCompiler); the fix removed it cleanly but the semantic now lives in a comment + two pinning tests — future changes should not reintroduce the entity fallback without reading that comment.

## Follow-ups (checkable)

- [x] **R1** — stage-transition required check uses stage-scoped metadata only; entity-policy-Exists no-warn pinned (StructuralAnalysisTests).
- [x] **R2** — dead `PublishStage` removed; class doc accurate.
- [x] **R3** — `EntryAssignedPropertyNames` published on ESM; exporter + analyzer share one computation.
- [x] **R4** — enum-keyword collision check widened to all keyword words; `EnumKeywordCollisionTests` added (error for type + structural keywords; capitalized forms valid; trailing comma + valid members unaffected).
- [ ] **R5 (future)** — a compile-smoke gate for the export (E-guard from the 2026-08-09 review) remains open; all render-based guards exist but nothing runs `dotnet build` in-suite.
