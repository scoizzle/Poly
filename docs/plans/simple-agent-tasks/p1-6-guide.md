# p1-6 — DSL guide honesty

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** task 5  
**Claimed by:** fleet agent p1-6 (opencode)

## Objective

Update `Poly.Mcp/Docs/poly-dsl-guide.md` so temporal vertical is documented as **shipped** with limits; remove or narrow "Not yet shipped: Date operations" if now false.

## Exact steps

1. Document: `Now`, `today` (if shipped), `N days` / `N months`, assign + policy compare.  
2. Document fail-closed: unknown units; pack-absent if applicable.  
3. Explicitly **not** shipped: schedule at, business days, TZ.  
4. Ensure `GetDslGuide_ReturnsProductSurface` / guide smoke still passes.  
5. No lab experiment grammar as product.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [x] Guide matches behavior  
- [x] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Docs/poly-dsl-guide.md` | Experiment docs as product |
| `Poly.Mcp/Docs/poly-dsl-agent-guide.md` | — |

## Status

**Status:** Done — implemented 2026-08-13 by p1-6 fleet agent (opencode).
Guides updated in both `poly-dsl-guide.md` and `poly-dsl-agent-guide.md`: temporal
vertical documented as shipped for authoring (assign RHS + policy compare, `Now`/`today`,
`N days`/`N months`, offset arithmetic) with explicit fail-closed (unknown units at parse,
pack-absent) and explicit NOT-shipped list (schedule at, business days, TZ). The residual
gap — runtime clock eval blocked on the fixed-clock `TimeProvider` seam
(`DirectVmAbiEmitter: unsupported node type NamedTypeReference`) — is documented, so the
guide does not claim runtime `Now` values. Verified against live MCP probes
(apply_dsl/export_dsl round-trip, unknown-unit parse rejection, simulate_policy runtime
failure). Full suite: 2145 passed.  
