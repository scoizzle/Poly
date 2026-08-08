# mcp-minify-0 — Inventory freeze (docs only)

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** none  

## Objective

Freeze keep/drop/unify lists for later tasks. **No product code.**

## Required reading

1. [`../mcp-catalog-minify.md`](../mcp-catalog-minify.md) §3–§4  
2. This suite README  

## Exact steps

1. Create file `docs/plans/simple-agent-tasks/mcp-minify-inventory-notes.md` with these sections (fill by grepping the tree):

### A. JSON expression call sites

Run from repo root:

```bash
rg -n "DomainExpressionJsonParser|ParseJson" --glob '*.cs'
```

Paste every path:line into the notes file.

### B. Per-type MCP add/remove tools

```bash
rg -n 'McpServerTool\(Name = "add_|McpServerTool\(Name = "remove_' Poly.Mcp --glob '*.cs'
```

List every tool name under **DELETE as separate tool**.

### C. Keep list

Copy parent plan §3.2 core table into notes. Confirm each still exists via:

```bash
rg -n 'McpServerTool\(Name =' Poly.Mcp --glob '*.cs'
```

### D. Test files that use JSON expressions

```bash
rg -n 'add_policy|"property".*"op"|expressionJson|DomainExpressionJsonParser' Poly.Tests --glob '*.cs'
```

List file paths that M2/M6 must update.

2. Mark parent plan M0 exit done in inventory notes (checkbox).  
3. Do **not** edit production code.  
4. Do **not** delete anything yet.

## Verification

- [ ] `mcp-minify-inventory-notes.md` exists with A–D filled  
- [ ] JSON call sites count ≥ 1 (today) documented  
- [ ] No production files changed (`git diff --stat` only notes + this task status)

## File ownership

| Edit | Do not edit |
|------|-------------|
| `docs/plans/simple-agent-tasks/mcp-minify-inventory-notes.md` (new) | `Poly/**`, `Poly.Mcp/**`, `Poly.Tests/**` |
| This task status + suite README row 0 | |

## Status

**Status:** Not Started  
