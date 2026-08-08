# p1-2 — Duration primary forms (`12 days`, `3 months`)

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1  

## Objective

Parse `N days` / `N months` (and optional `weeks` if trivial) as expressions that participate in `Now - 12 days` → `DateOperation`.

## Exact steps

1. Implement form(s) that match: **Number** then Identifier unit in `{days, day, months, month}` (document singular/plural accepted set).  
2. Produce IR that `DateOperation` can consume (offset literal + unit → kind AddDays/AddMonths).  
3. Binary `Now - <duration>` or `DueDate + <duration>`: either  
   - parse as normal Subtract/Add of Now and duration then **analysis/rewrite** to DateOperation, or  
   - specialize in form/parser — prefer **keep arithmetic nodes** + analysis resolve if already patterned; else rewrite in form when left is Now.  
   - Design lock: map to `DateOperation`. Choose smallest path; document in notes.  

4. Fail closed unknown unit at parse **or** analysis — task 4 may own analysis; if parse-time, throw/form returns false and number stays literal (bad). Prefer: form only matches known units; `12 fortnights` → number + property fortnights OR analysis error.  
   - **Required:** `12 fortnights` does **not** produce successful DateOperation.  

5. Tests:
   - `Duration_12Days_Form_Parses`  
   - `Now_Minus_12Days_BecomesDateOperation` (parse and/or after analysis)  
   - `UnknownUnit_Fortnights_DoesNotSucceedAsTemporal`

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Duration + Now arithmetic vertical green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| Expression forms, DomainExpression, analysis/lowering as needed | P9 schedule |
| Tests | MCP minify |

## Status

**Status:** Not Started  
