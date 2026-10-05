# pack-3c-2 — Minimal API as root host contributor

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** pack-3c-1 `[x]`  
**Claimed by:** fleet agent pack-3c-2 (opencode) 2026-08-13

## Objective

Minimal API + `.http` register as contributors. They emit the **composition root** only (the domain compiled). No child-entity route union.

## Exact steps

1. Existing MinimalApiGeneratorTests stay green.
2. Wire generators as `IArtifactContributor`. CompileMode.All uses the hook.
3. Test: a suite with parent + produced billing contract does **not** emit Ledger routes.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*MinimalApi*"
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| `src/Poly.DslCompiler/MinimalApiGenerator.cs` | EF DbContext internals |
| `HttpFileGenerator.cs` | temporal pack |

## Status

**Status:** Done — MinimalApi + .http register as `IArtifactContributor` via `MinimalApiHostArtifactContributor`; CompileMode.All emits Program.cs + demo.http through the hook; composition-root-only test (parent + produced billing contract) emits no Ledger routes.
