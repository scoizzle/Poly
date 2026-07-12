# Micro-Task: Agent-facing EvolutionTrace reading guide

**Parent Workstream**: WS4  
**Difficulty**: Small Model Friendly  
**Estimated Tokens**: ~4k  
**Status**: [ ] Not Started

## Objective

Write a short guide for agents/MCP consumers: how to interpret `EvolutionResult`, `EvolutionTrace`, `WasRolledBack`, and `EVOLUTION_STEP` diagnostics.

## Context You Need

- `Poly/DomainModeling/Evolution/DomainEvolution.cs` (Apply path)
- `Poly/DomainModeling/Evolution/EvolutionResult.cs`
- `Poly/DomainModeling/Evolution/EvolutionTrace.cs`
- One successful + one rollback test under `Poly.Tests/DomainModeling/Evolution/`

## Exact Steps

1. Read Apply success vs rollback paths.
2. Document in `docs/plans/v2-to-v3/spikes/agent-evolution-trace-guide.md` (create file):
   - Field meanings
   - How to detect rollback
   - Where step history appears (trace vs diagnostics)
   - Example success and failure (minimal pseudo-output)
3. Keep under ~100 lines. No code changes required unless a field is misnamed in public API docs.

## Verification

- [ ] Guide matches actual type names and property names in code
- [ ] Mentions both success and rollback
- [ ] Links back to evolution design decision

## Output

- `docs/plans/v2-to-v3/spikes/agent-evolution-trace-guide.md`
- Agent summary

## Out of Scope

- Redesigning traces
- MCP tool changes
