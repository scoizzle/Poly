# gpure-5 — Effect statement grammar + wire

**Difficulty:** L  
**Status:** `[ ]`  
**Prereq:** task 4 (expr must work for assign/if conditions)  

## Objective

Effect statements (`assign`, `transition`, `create`, `delete`, `invoke`, `if`) are chosen by **Matcher** over an `effect` rule; handlers produce existing `Effect` IR. Remove hand first-token switch as the language.

## Required reading

1. `PolyDslParser.ParseEffect` and callees  
2. `DslGrammar` / pattern style for entity-body  
3. Product effect keywords in `DslTokenKind`  

## Exact steps

1. Add `DslGrammar` rule `effect` with patterns (names illustrative — use clear names):

| Pattern name | First tokens (approx) |
|--------------|------------------------|
| `assign` | Assign … |
| `transition` | Transition … |
| `create` / `create-in` | Create [In] … |
| `delete` | Delete |
| `invoke` | Invoke … |
| `if` | If … |

2. Nested effect lists (if body, subscription body): use `Many("effect")` or loop MatchRule("effect") until fail — **loop is OK** if each statement is table-selected.

3. Replace `ParseEffect` entry with:

```text
match = MatchRule("effect")
switch pattern → existing handler methods (may rename to HandleAssignEffect etc.)
```

4. Handlers may still call expression parse (Grammar-wired from task 4) and Expect tokens for tails not in the pattern — **minimize** tails; push into patterns when cheap.

5. Tests: existing effect/round-trip/MCP apply_dsl must stay green. Add:

   - `EffectGrammar_Assign_MatchName` (optional unit on pattern name)  
   - Rely on corpus for semantics  

6. Grep: no large `switch (_current.Kind)` as sole effect language entry — pattern dispatch first.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
rg -n "private Effect ParseEffect" Poly/DomainModeling/Parsing -A 30
# Entry should MatchRule effect first
```

- [ ] Effect entry is Matcher-driven  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DslGrammar.cs`, `PolyDslParser.cs` effect paths | Temporal product features |
| Tests as needed | |

## Status

**Status:** Not Started  
