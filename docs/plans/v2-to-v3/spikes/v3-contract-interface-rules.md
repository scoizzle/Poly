# V3 Contract Interface Generation — Rules (WP9 Prep)

**Date:** 2026-07-10  
**Status:** Spike (no implementation)  
**Purpose:** Capture contract interface generation rules from AGENTS.md + git history for a future V3 implementer.  
**Related:** AGENTS.md **Contract Interface Generation** section; `docs/decisions/2026-06-08-domain-lowering-boundary.md`

## Rules (from AGENTS.md, authoritative)

| Aspect | Rule |
|--------|------|
| **Naming** | `I{StageName}{EntityName}` |
| **Inheritance** | Entity base interface + parent stage interface when `Stage.Parent` is set |
| **Abstract stages** | Kept alongside concrete children |
| **Action placement** | Only direct actions when a parent stage interface exists in the inheritance chain; otherwise all effective actions |

## Recovered context (from V2 source history)

The deleted V2 `DomainImplementationLoweringPass.LowerToContractInterfaces()` generated C# interface definitions
for each (entity, stage) pair. Key patterns:
- Each stage on an entity produced one interface
- Stage parent hierarchy was mirrored in interface inheritance
- Actions on a stage became method signatures on that stage's interface
- Entity-level actions (not assigned to any stage) went on a base interface
- Policy guards were not part of the contract interface surface (they're VM-evaluated at runtime)

## V3 architecture considerations

| Concern | Guidance |
|---------|----------|
| **Home** | `Poly/DomainModeling/Lowering/` or new `Poly/DomainModeling/CodeGen/` — follow placement rules (AGENTS.md) |
| **Input** | `Domain` + `AnalysisResult` (same as `DomainExpressionLoweringPass`) |
| **Output** | Syntax AST nodes representing interface declarations (not raw C# strings). The `CSharpGenerator` already handles Syntax → C# text. |
| **Boundary** | Domain → generic AST only — no domain-specific opcodes (`docs/decisions/2026-06-08-domain-lowering-boundary.md`) |
| **Consumer** | Pull when an external tool or MCP tool needs `I{Stage}{Entity}` for codegen preview or policy binding |

## Smallest first V3 deliverable

```csharp
// Given: Entity "Order" with stage "Draft" containing action "Submit"
// Emit (as Syntax AST):
interface IDraftOrder {
    void Submit();
}
```

One entity, one stage, one method interface. No inheritance chains, no abstract stages,
no effective-action computation. This proves the lower-to-interface pattern works.

## Non-goals (keep out of M2)

- Codegen implementation
- Full stage hierarchy resolution
- Abstract stage filtering
- Effective action computation (all-actions vs direct-actions rule)
- C# file output

## Related decisions

- `docs/decisions/2026-06-08-domain-lowering-boundary.md` — domain lowers to generic AST only
- AGENTS.md Contract Interface Generation — naming/inheritance/placement rules
