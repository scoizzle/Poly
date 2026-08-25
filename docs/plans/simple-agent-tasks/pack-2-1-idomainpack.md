# pack-2-1 — IDomainPack + PackSet + AddPack

**Difficulty:** M  
**Status:** `[ ]`  
**Wave:** C · **Prereq:** pack-1-gate  

## Objective

One apply entry. Duplicate pack id fails closed. DomainInputBuilder can AddPack.

## Exact steps

1. Failing tests `Poly.Tests/DomainModeling/Packs/DomainPackTests.cs`:
   - `AddPack_DuplicateId_Throws`
   - `AddPack_AppliesAnnotationsAndTypeMaps`
2. `[NEW]` `Poly/DomainModeling/Packs/IDomainPack.cs`:
   ```csharp
   public interface IDomainPack {
       string Id { get; }
       void Apply(PackContext context);
   }
   ```
   `PackContext` exposes the existing `DomainInputBuilder` surfaces (Annotations, ExpressionForms, TypeMaps, AddStorageConvention, AddAnalysisPass) plus `ExpressionPrintRegistry` when present. Do **not** add produce/artifact hooks yet.
3. `[NEW]` `PackSet` — ordered list, `Add` fail-closed on duplicate `Id` (ordinal).
4. `[MODIFY]` `DomainInputBuilder.AddPack(IDomainPack)` applies immediately into the builder. `Build()` uses the same inputs for parser and analysis.
5. No DBMS re-home in this task (2-2..2-4).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DomainPack*"
```

- [x] Duplicate id throws  
- [x] Apply mutates builder  
- [x] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `[NEW] Poly/DomainModeling/Packs/**` | `src/Poly.Packs.*` |
| `Poly/DomainModeling/DomainInputSet.cs` | `DslCompiler.cs` |
| `[NEW] Poly.Tests/DomainModeling/Packs/**` | MCP |

## Status

**Status:** `[x]`  
**Claimed by: opencode (pack-2-1-idomainpack)**
