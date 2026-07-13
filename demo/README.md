# Poly Demo — 60-Second Quickstart

```bash
dotnet run --project demo
```

## What it does

One file (`Program.cs`) that proves the full platform end-to-end in ~65 lines:

| Step | What | Platform piece |
|------|------|---------------|
| 1 | Define a `Person` entity with properties, stages, and an action | `DomainEvolution.Evolve()` |
| 2 | Attach `IsAdult` and `IsActive` guard policies | `DomainExpression` → `AddPolicyToEntity` |
| 3 | Create an instance (Alice, 25, active) | `DomainEntityInstance.Create()` |
| 4 | Evaluate both policies via the VM | `instance.EvaluatePolicy()` → `Interpreter.Compile` |
| 5 | Call `Activate` action → stage transition Draft→Active | `instance.CallAction()` + `StageTransitionEffect` |

## Output

```
Bootstrapped: Demo
  Entity:  Person
  Stages:  Draft, Active

Instance: Alice, Age 25
  Stage:       Draft

  IsAdult (Age >= 18)?  True
  IsActive (Active==true)? True

  CallAction("Activate"): True
  New stage:      Active
  Current stage:  Active

=== Summary ===
Domain → Policy → Instance → Evaluate → Action → Stage transition
All paths green.  The platform works end-to-end.
```

## Where to go next

- **[AGENTS.md](../AGENTS.md)** — Principles, placement rules, build/test commands
- **[docs/CORE.md](../docs/CORE.md)** — Platform map: ownership, machinery, "use this / not that"
- **[Poly/Introspection/README.md](../Poly/Introspection/README.md)** — Type/member model and multi-host design
- **[docs/plans/ast-types-provider-instance-ergonomics.md](../docs/plans/ast-types-provider-instance-ergonomics.md)** — How AST types, instances, and MCP tools are layered
