# P4-4 — Product guide honesty

**Difficulty:** S  
**Status:** `[x]` — DONE 2026-08-06

## Implementation notes

- `Poly.Mcp/Docs/poly-dsl-guide.md` § 7: grammar updated to
  `"when" ["any"|"all"] relationship-name stage-name …`; new **Quantifier** subsection
  documents the three modes (`Each` default, `any` fires once when ≥1 linked target
  matches, `all` fires once when every linked target matches), set-state-after-
  transition semantics, singular+Any/All analysis warning, and peer `as` validity.
  Added a poly example with all three forms.
- Guide is an **embedded resource** (`Poly.Mcp.Docs.poly-dsl-guide.md`) — rebuilt
  Poly.Mcp via test-project build so `GetDslGuide` serves the new text.
- Smoke tests green: `GetDslGuide_ReturnsProductSurface` (surface keywords + no
  `require {` anti-pattern) and `GetDslGuide_GoldenExample_AppliesCleanly` (golden
  example extraction unchanged — it targets `## 11. Example (Round-Trip Safe)`).
- Verified: 1855/1855 green after guide rebuild.
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
