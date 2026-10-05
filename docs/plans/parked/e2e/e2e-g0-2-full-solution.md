# e2e-g0-2 — run-probe compiles the full solution

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** g0-1  
**Fleet:** P0-0b, P0-0d  

## Objective

`scripts/run-probe.sh` (and `discovery-round.sh` PASS) compile entities + `Program.cs` + DbContext against ASP.NET + EF reference assemblies. PASS = 0 errors / 0 warnings, not `^errors: 0` on entities-only.

## Exact steps

1. Read `scripts/run-probe.sh` and `scripts/discovery-round.sh`.
2. Add `--mode all` / `--dbms sqlite` (or equivalent) compile pass. Reuse DslCompiler `CompileMode.All` + probe-check on **each** emitted `.cs`, or one compilation that includes them. Look at `demo/Poly.RestApi` for the reference set you need.
3. Change discovery-round PASS to full-solution 0/0.
4. After this task, 09/13 probes **should fail loud** (that is success for the gate). Do not “fix” warehouse/clinic in this task.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `scripts/run-probe.sh` | `MinimalApiGenerator.cs` |
| `scripts/discovery-round.sh` | `DbContextGenerator.cs` |
| `scripts/probe-check/**` if it must accept multiple files | product lowering |

## Status

**Status:** Not Started  
**Claimed by:**  
