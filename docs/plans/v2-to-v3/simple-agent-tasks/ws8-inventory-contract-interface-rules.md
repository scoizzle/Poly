# Micro-Task: Inventory contract interface rules (WP9 prep)

**Parent**: WS8 / WP9  
**Suite:** [`ws8-README.md`](ws8-README.md) **#5** (lowest priority active)  
**Difficulty**: Small  
**Estimated Tokens**: ~5k  
**Status**: [x] **Done** — spike created at `docs/plans/v2-to-v3/spikes/v3-contract-interface-rules.md`. Rules extracted from AGENTS.md. Smallest deliverable defined (one entity, one stage, one interface). No implementation.

## Objective

Document **contract interface generation rules** for a future V3 implementation. **V2 source is deleted** — do not expect `Poly/Data/Modeling` on disk.

## Context

- AGENTS.md **Contract Interface Generation** (authoritative naming/inheritance/placement)
- Git history if needed: `git log --all --full-history -- '**/DomainImplementationLowering*'` or similar
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`

## Exact Steps

1. Extract rules from AGENTS.md into a table:
   - Naming: `I{StageName}{EntityName}`
   - Inheritance: entity base + parent stage interface when `Stage.Parent` set; abstract stages kept
   - Action placement: only direct actions when parent stage interface exists; else all effective actions
2. Optionally recover notes from git history of deleted V2 `LowerToContractInterfaces` (paths/line intent only).
3. Write `docs/plans/v2-to-v3/spikes/v3-contract-interface-rules.md` (≤ ~150 lines).
4. Propose **smallest** first V3 deliverable (one entity, one stage → one interface AST node) — do **not** implement.

## Verification

- [ ] Spike file exists
- [ ] Rules match AGENTS.md
- [ ] No generator implementation in this task

## Out of Scope

- Implementing codegen
- Changing AGENTS.md rules

## Supersedes

- Older task name `ws8-inventory-v2-contract-interface-rules.md` (V2 path assumptions) — use **this** file instead.
