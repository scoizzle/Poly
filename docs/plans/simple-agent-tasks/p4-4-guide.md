# P4-4 — Product guide honesty

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** P4-1–P4-3 product ACs  

## Objective

Update `Poly.Mcp/Docs/poly-dsl-guide.md` (and embedded get_dsl_guide source if same file) with `when any|all` syntax, default Each, peer `as` note, cardinality rules.

## Required reading

- Current guide `when` section  
- AGENTS: keep guide in sync with parser  

## Exact steps

1. Document grammar + examples.  
2. Document empty-set / set-state semantics briefly.  
3. Ensure GetDslGuide smoke still passes if present.

## Verification

- [ ] Guide examples match parse  
- [ ] Smoke / honesty tests green  

## File ownership

- **Edit:** poly-dsl-guide.md (+ MCP guide serve path if separate)  
- **Do not edit:** runtime  

## Status

**Status:** Not Started  
