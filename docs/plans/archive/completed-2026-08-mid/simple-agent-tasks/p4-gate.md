# P4 suite gate

**Suite:** [`p4-README.md`](./p4-README.md)  
**Status:** `[x]` — PASSED 2026-08-06

| ID | Check | Status |
|----|--------|--------|
| G1 | Parse/print `when any|all Rel Stage` + optional `as name`; omit = Each | `[x]` |
| G2 | Analysis: singular + Any/All fail closed or existing codes | `[x]` |
| G3 | Runtime goldens: Any (and All if cheap); Each regression | `[x]` |
| G4 | Guide updated same change | `[x]` |
| G5 | Build + suite green; pre-ship | `[x]` |

## Notes

- G1: p4-1 — `ParseSubscriptionQuantifier` in both `when` sites (stage + entity-level);
  printer emits keyword only for non-Each. 4 round-trip tests.
- G2: p4-2 — diagnostics already existed (`isSingularFromSource` incl. ManyToOne →
  `SubscriptionContractMismatch`); added DSL-level fail-closed tests (Any on one→error,
  Any/All on many→no error). **F5 (review):** singular + Any/All promoted from warning
  to error in `SubscriptionAnalyzer`; IR test asserts `DiagnosticSeverity.Error`, DSL
  test asserts fail-closed evolve rejection (DMSS003).
- G3: p4-3 — 3 goldens in `P4SubscriptionQuantifierDslTests` (Any fire-once, All
  set-state, Each per-transition with peer). Zero runtime edits (hard rule honored).
- G4: p4-4 — guide §7 grammar + quantifier table + set-state semantics + example;
  embedded resource rebuilt; both `GetDslGuide` smoke tests green.
- G5: 1855/1855 green; pre-ship review completed — no 🔴/🟠 findings (runtime
  untouched; parser/printer round-trip verified; guide same-change).
