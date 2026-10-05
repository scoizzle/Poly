# e2e-0-1 — Guide honesty sweep

**Difficulty:** M  
**Status:** `[ ]`  
**Fleet:** P4-5, L2, L3, L5 wording  

## Objective

`Poly.Mcp/Docs/poly-dsl-guide.md` matches the shipped surface. Agents stop treating IR-only / deleted / wrongly-stubbed constructs as product syntax.

## Required reading

- Parent slice 0 + L2, L3, L5  
- `Poly.Mcp/Docs/poly-dsl-guide.md` §0.3, §0.4, §6, §7, §8, §9, §11, “Not yet shipped”, “Shipped-surface boundaries”  
- Confirm throw arms: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` (Q3′ only vs path-prefix / Rel exists)

## Exact steps

1. **L3:** state that action params are bare identifiers (`PropertyAccess`). Analysis/lowering/bindings must treat an in-scope param name as a parameter. No `param` keyword. Do not call `ParameterAccess` a product authoring form.
2. **L2:** keep OwnedAccess IR-only; path-prefix is the product spelling.
3. **Export bullet:** path-prefix and `Rel exists` lower; **only** `any`/`all`/`none`/`count` still throw `NotSupportedException` in standalone C# export. Do not claim path-prefix export still throws.
4. Fix stale examples (fleet P4-5): §8 decimal / invoke any-all; §11 inline `enum(...)`; §0.4 `;` create-in (whitespace is the parser); §6 dotted binder args; §0.3 DMEFF011 + to-one claims; §9 `unlink_instances` — linker is MCP `link_instances` / `store.Link`; no Unlink Effect IR; duplicate-annotation last-wins (not “parse error”) if that is the code.
5. Date operations stay “not yet shipped — owned by p1.” VM AddDays is not listed as a gap.
6. Add or extend `GetDslGuide_ReturnsProductSurface` / example compile smoke only if an existing test already pins guide text — do not invent a guide-test framework.

## Verification

- [ ] No guide sentence claims Link/Unlink/Delete **Effect IR**  
- [ ] Q3′ export throw is the only store-dependent export throw named  
- [ ] L3 matches parent lock  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Docs/poly-dsl-guide.md` | `Poly/**` product code |
| existing guide smoke test only if already present | inventories (task 2) |

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** 2063/2063 green; guide + agent-guide swept; golden + corrected examples apply clean
