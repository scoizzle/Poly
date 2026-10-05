# mut-safety-0 — Inventory (docs only)

**Difficulty:** S  
**Status:** `[ ]`  

## Objective

Locate Evolve / session write paths. No product behavior change.

## Exact steps

1. Create `docs/plans/simple-agent-tasks/mut-safety-inventory-notes.md`.

2. Document with greps:

```bash
rg -n "class McpSessionStore|void Evolve|TryGet|Update\(" Poly.Mcp --glob '*.cs'
rg -n "McpSessionStore\.Evolve" Poly.Mcp --glob '*.cs'
```

List:
- Path to `McpSessionStore`  
- How `Evolve` does read → mutate → write  
- Whether `TryGet` is unlocked today  

3. Note current `DomainToolResponse` fields (Success, Message, Revision, Data).  
4. List tools that call Evolve (mutation tools).  

## Verification

- [ ] Notes file exists with Evolve path described  
- [ ] No production code changes  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `mut-safety-inventory-notes.md` | `Poly/**`, `Poly.Mcp/**` code |

## Status

**Status:** Not Started  
