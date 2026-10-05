# e2e-0-3 — Domain + CompileMode XML honesty

**Difficulty:** S  
**Status:** `[ ]`  

## Objective

XML comments stop claiming types/modes that do not exist or are already implemented.

## Exact steps

1. `Poly/DomainModeling/Domain.cs`: XML must not say a Domain aggregates **events** as a DomainType. Types are Entity, ValueType, PrimitiveType, EnumType (+ ImportedContracts / ContractBindings as fields).
2. `src/Poly.DslCompiler/DslCompiler.cs` `CompileMode.All`: remove “Not yet implemented” if `GenerateAllFiles` already emits `Program.cs` + `demo.http`. Comment must say what All emits.

## Verification

- [ ] Those two comments match code  
- [ ] `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Domain.cs` (XML only) | Domain members / behavior |
| `src/Poly.DslCompiler/DslCompiler.cs` (`CompileMode` XML only) | generators |

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** Domain.cs + DomainType.cs no Event; CompileMode.All matches GenerateAllFiles; build 0/0
