# pack-3c-1 — IArtifactContributor

**Difficulty:** M  
**Status:** `[x]`  
**Claimed by:** fleet agent pack-3c-1 (2026-08-13)  

## Objective

Packs may emit files from the **analyzed** domain. Compiler asks contributors; analysis failures still fail closed first.

## Exact steps

1. Failing test: a test contributor emits `hello.txt`; compile All includes it; structural analysis failure emits nothing.
2. `[NEW]` `IArtifactContributor` — `IReadOnlyList<(string FileName, string Source)> Contribute(analyzed domain + analysis result)`.
3. `PackContext` / compiler registry. DslCompiler invokes after analysis.
4. Do not move entity C# export into a pack.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*Artifact*"
```

## File ownership

| Edit | Do not edit |
|------|-------------|
| `[NEW]` contributor types | `DomainToCSharpExporter.cs` |
| `DslCompiler.cs` invoke only | bind lowering |

## Status

**Status:** Done — IArtifactContributor added, DslCompiler invokes after analysis, fail-closed first.
