# create-create-in-4 — Store reads in the tree

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 3

## Objective

Store-aware `Rel exists`, quantifiers, and path-prefix are Store calls in the lowered program. Execute-time `PreprocessQuantifiers` → literals is Domain walk beside the tree — remove it from simulate.

## Required reading

1. Parent slice 4 + L3 / L6 / L9
2. `DomainEntityInstance.PreprocessQuantifiers` / `QuantifierPreprocessRewrite`
3. Unique Notify-shaped bind (cannot Member-read Store from dictionary `This`)
4. Fail-closed: missing Store / missing link / collection used as to-one must throw — no vacuous true

## Exact steps

1. Failing test: an action (or named policy) whose condition is `Rel exists` / `any Rel` lowers to an `Invoke` of a Store-read method on `This`, not a `Constant` of a precomputed bool. After `create in Rel` in the same action, a later `Rel exists` sees the new child (literals frozen at preprocess cannot).
2. Notify-shaped Store reads (`Exists`, `GetRelated`, `Any` / `All` / `Count` — name for what they are). Delegate to `DomainInstanceStore`. List on the type def.
3. Lower in `DomainExpressionLoweringPass` (or Effect condition lowering). Delete execute-time preprocess from `ExecuteEffectList` / `EvaluatePolicy`.
4. If a rewrite to literals is tempting: it belongs in lowering, and it is **wrong** when the same operation mutates the graph then reads it. Prefer Store reads. Do not keep execute-time rewrite.
5. Action-parameter roots (`lead Name` in the bag) stay bag Member reads — they are not store hops.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*Quantif*|/*Exists*|/*PathPrefix*"
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false
```

- [ ] No `PreprocessQuantifiers` on the simulate / invoke path
- [ ] Missing Store / missing link fail closed
- [ ] Create-then-exists in one action sees the child

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` | MCP tools (slice 5) |
| `Poly/DomainModeling/Runtime/DomainEntityInstance*.cs` | C# export Stay.Create goldens |
| `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` | EF |

## Status

**Status:** Not Started
