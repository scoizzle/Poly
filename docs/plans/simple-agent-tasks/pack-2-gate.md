# pack-2-gate — Phase 2 done

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** pack-2-1 … pack-2-6 `[x]`  

## Exact steps

1. Adding a pack is `AddPack`, not a new `DbmsPack` arm.
2. apply_dsl / export_dsl share PackSet.
3. pr1 on phase-2 dirty files.
4. Full build + suite. Update CORE one sentence: host composes packs.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

- [x] Suite green  
- [x] Mark pack-2-README Done  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `docs/CORE.md` (one host sentence) | new pack features |
| task checkboxes | |

## Status

**Status:** `[x]` Done — build + suite green (2114/2114); pr1 clean on phase-2 files; CORE updated  
**Claimed by:** opencode (pack-2-gate)  

Gate results (2026-08-13):
1. Adding a pack is `AddPack`, not a new `DbmsPack` arm — verified: `DslCompiler.Compile(..., params IDomainPack[])` (DslCompiler.cs:82) + `CreateInputs(params IDomainPack[])` (DslCompiler.cs:198) apply packs via `DomainInputBuilder.AddPack`; `DbmsPack` remains a three-arm convenience alias mapping to `SqlitePack`/`SqlServerPack` (DslCompiler.cs:210), with no new enum arms.
2. apply_dsl / export_dsl share PackSet — verified: session holds one input bundle (`McpSessionState.ParserInputs`, built once per session); `apply_dsl` parses with `state.ParserInputs` (DomainTools.cs:1341), `export_dsl` prints with `state.ParserInputs` (DomainTools.cs:1425). `McpPackSharedInputsTests` proves a pack expression form round-trips through both.
3. pr1 on phase-2 dirty files — clean: no 🔴 structure or 🟠 contract findings; duplicate pack id / unknown DbmsPack fail closed; `ResolveDbms` generic fallback is documented provider-selection-only.
4. `dotnet build Poly.Benchmarks/...` passes; `dotnet run --project Poly.Tests/...` 2114/2114 green. CORE §3.6 updated: "Hosts compose packs."
