# mcp-minify-1 — DSL expression fragment API

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 0 `[x]`

**Done 2026-08-08:** `DslExpressionFragment.ParseExpressionFragment` (static, in `Poly/DomainModeling/Parsing/DslExpressionFragment.cs`) + `DslExpressionFragmentTests.cs` (5 cases). Empty/trailing fail closed with `GrammarException` (a `FormatException`); open-form registry honored via `inputs?.ExpressionForms`. Suite 1935 green.  

## Objective

Public API parses a **standalone DSL expression** (e.g. `Age >= 18`) into `DomainExpression`, with fail-closed trailing junk. Used by unified `add` (policy) and oracles.

## Required reading

1. `Poly/DomainModeling/Parsing/DslExpressionParser.cs`  
2. `Poly/DomainModeling/Parsing/PolyDslParser.cs` (cursor / ctor with `DomainParserInputs`)  
3. `Poly/DomainModeling/Parsing/DslTokenReader.cs`  
4. Parent plan §5 M1  

## Exact steps

1. Add a **public static** entry point (preferred location: `PolyDslParser` or new file `DslExpressionFragment.cs` in `Poly/DomainModeling/Parsing/`):

```csharp
// Exact public signature required (name may be ParseExpressionFragment on a static helper type):
public static DomainExpression ParseExpressionFragment(
    string expression,
    DomainParserInputs? inputs = null)
```

2. Behavior **must**:
   - Reject null/whitespace with clear exception message containing `empty` or `must not be empty`.  
   - Tokenize with `DslTokenReader`.  
   - Use `DslExpressionParser` + same dual-cursor pattern as `PolyDslParser` (implement a small private cursor class **or** reuse parser internals — do not reimplement precedence).  
   - Pass `inputs?.ExpressionForms` into `DslExpressionParser`.  
   - After successful parse, **require next token is EndOfFile**; else throw with message containing `trailing` or `unexpected`.  
   - Rethrow / wrap parse errors as `FormatException` or `GrammarException` (either OK; tests assert message content loosely).

3. Add tests file: `Poly.Tests/DomainModeling/Parsing/DslExpressionFragmentTests.cs`  
   TUnit style. Exact cases:

| Test name | Input | Expect |
|-----------|--------|--------|
| `Fragment_AgeGte18_IsComparison` | `Age >= 18` | `Comparison` with `PropertyAccess` left, kind ≥ / Gte |
| `Fragment_AndOr_Parses` | `(A == 1) and (B == 2)` or product-true and syntax | succeeds as composite |
| `Fragment_Empty_Throws` | `""` / `"   "` | throws |
| `Fragment_TrailingJunk_Throws` | `Age >= 18 leftover` | throws |
| `Fragment_OpenForm_Registry_Honored` | register a form that maps `MAGIC` → literal 42 (copy pattern from `DslExpressionE1Tests`), parse `MAGIC == 42` with inputs | left is Literal 42 |

4. Do **not** change MCP tools in this task.  
5. Do **not** delete `DomainExpressionJsonParser` yet.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build -- --treenode-filter '/*/*/*Fragment*'
# or full suite if filter fails:
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Public API exists and is callable from tests without InternalsVisibleTo hacks  
- [ ] All five cases above green  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/*` (fragment API only) | `Poly.Mcp/**` |
| `Poly.Tests/DomainModeling/Parsing/DslExpressionFragmentTests.cs` | Delete JsonParser; oracle tools |

## Status

**Status:** Done  
