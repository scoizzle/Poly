# Live demo reliability

**Date:** 2026-08-13  
**Kind:** Execution cut of [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md). Not ValueType/contracts/p1.  
**Bar:** Sit down, compile a `.poly`, `dotnet run`, hit HTTP. Do that twice in a row.

## What “live demo” means

One command:

```bash
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- \
  --mode all --dbms sqlite <demo>.poly /tmp/poly-live &&
dotnet run --project /tmp/poly-live
```

Then `demo.http` (or curl) creates a root, invokes an action, reads a child. No CS*, no silent empty seed, no `NotSupportedException` on the actions you show.

**Flagship domains** (measured 2026-08-13, `--mode all --dbms sqlite`, TreatWarningsAsErrors):

| Domain | Result |
|--------|--------|
| `probes/fleet-eval/13-packs/library.poly` | **Builds** (no child actions — too thin to demo) |
| `probes/fleet-eval/09-transport/warehouse.poly` | CS0103 `dto` on child `Load`; CS8602 back-ref |
| `probes/fleet-eval/09-transport/orders.poly` | CS0103 `dto` on child actions |
| `probes/fleet-eval/09-transport/clinic.poly` | `dto` + duplicate `{id}` + `.Collection()` on to-one |
| `probes/fleet-eval/12-mcp/mcp-library.poly` | duplicate `{id}` |
| `demo/Poly.RestApi` | Hand-written — not the customer path |

**Progress 2026-08-13:** warehouse, orders, clinic, mcp-library **full-solution compile**. `scripts/live-demo.sh` walks warehouse create → GET → register truck (HTTP 200). Seed/`demo.http` still emit values that fail `pattern`/`range` (next).

## Cut (do these, stop)

1. **Generate a compilable host** for warehouse + orders + clinic + mcp-library (e2e-4-1…4-5).  
2. **Compile oracle** in-suite (`DslCompilerCompileOracleTests`) for those four files — the demo gate.  
3. **Seed + demo.http** honor `pattern`/`required`/`range` so POST is not 400 (e2e-4-7).  
4. **Q3′ policies** do not make the actions you show throw (e2e-2). Keep demo domains free of entity-level `all Rel` guards until that lands — or fix export.  
5. **One scripted walk** (`scripts/live-demo.sh`): compile warehouse → run → curl create warehouse → register truck → GET. Fail loud.

Out of this cut: ValueType, contracts, DateOperation authoring, uniqueness EF indexes, printer parity, MCP session lock.

## Tasking

Use existing [`simple-agent-tasks/e2e-4-README.md`](simple-agent-tasks/e2e-4-README.md) for generator work. Oracle + `live-demo.sh` live in this cut.
