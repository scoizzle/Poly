# gpure-0 — Inventory RD residual + Grammar gaps

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** none  

## Objective

Document every product parse path that is still recursive-descent language, and list Grammar engine gaps. **No production code.**

## Exact steps

1. Create `docs/plans/simple-agent-tasks/gpure-inventory-notes.md`.

2. Fill section **A. RD residual** by grepping and reading:

```bash
rg -n "private DomainExpression Parse|private Effect Parse|ParseOr|ParseAnd|ParsePrimary|ParseEffect" \
  Poly/DomainModeling/Parsing --glob '*.cs'
```

Table columns: Method | File | Role (expr layer / effect / other) | Target Grammar rule name (proposed).

3. Fill section **B. Already Matcher-driven**:

```bash
rg -n "MatchRule\(|TryMatch\(" Poly/DomainModeling/Parsing --glob '*.cs'
```

4. Fill section **C. Grammar engine gaps** — copy parent §4 and mark each:

| Gap | Needed for pure? (Y/N) | Proposed engine feature name |
|-----|------------------------|------------------------------|
| Recursive single rule ref | | `Rule(ruleName)` |
| Left-assoc binary chain | | `LeftAssoc` or similar |
| … | | |

5. Fill section **D. File ownership map** for later tasks (who owns Grammar vs DslGrammar vs PolyDslParser).

6. Do **not** edit `Poly/**` code.

## Verification

- [ ] Notes file exists with A–D  
- [ ] `git diff --stat` shows only docs under `simple-agent-tasks/`  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `gpure-inventory-notes.md` (new) | `Poly/**`, `Poly.Mcp/**` |

## Status

**Status:** Not Started  
