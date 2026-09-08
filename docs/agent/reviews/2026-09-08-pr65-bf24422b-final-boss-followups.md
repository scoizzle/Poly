# Final Boss follow-ups — PR 65 `bf24422b` — 2026-09-08

Source review: [`2026-09-08-pr65-bf24422b-final-boss.md`](2026-09-08-pr65-bf24422b-final-boss.md).

**Owner:** none blocking. F6 (nested/else-if `assignedRhs` inherit) is closed on this SHA. F1–F5 oracles from efa5ab7b are present. F7 is coverage for then→else isolation; do not block Foreman merge. Do not implement inside the review branch.

---

## Open bugs (must close before ship)

None.

---

## Open suggestions

- [ ] **F7** — `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:1307-1354` — Else-if inherit of ancestor `OpenStays+1` is tested with an **empty then**. Add a Nights oracle that would fail if then-assigns leaked into `elseRhs`: e.g. `assign OpenStays to OpenStays + 1` then `if (confirm) { assign OpenStays to 0 } else if (OpenStays >= 1) { create Stay { Nights: nights } }` with confirm=false, nights=0 → `Succeeded==false`, ErrorMessage contains Nights, **OpenStays==0**, inner probe guard still `OpenStays + 1` (not `0 >= 1`). Product path already uses separate dicts (`EffectLoweringPass.cs:818-820` vs `:831-833`).

---

## Disposition / prior (efa5ab7b F1–F6)

| Item | Disposition | Evidence this SHA |
|------|-------------|-------------------|
| F6 nested/else-if empty then/else dict | **fixed** | `EffectLoweringPass.cs:818-820` / `:831-833` copy `priorAssignRhs`; InvokeAction nested `:1254` and else-if `:1307` Nights + OpenStays==0; export `:2461` / `:2497` `OpenStays + 1` before ProbeCreate |
| F1 export guard + AssignFalse `if (false)` | **fixed** | `DomainToCSharpExporterTests.cs:2416-2420` / `:2425-2457` |
| F2 Item4 runtime | **fixed** | `Item4FailBeforeMutateTests.cs:82-92` ModuleBook nights=0 OpenStays==0 |
| F3 ConditionDrift ProbeCreate-before-assign | **fixed** | `ActionEntityReturnTests.cs:738-751` |
| F4 quantifier subst skip | **fixed** | `EffectLoweringPass.cs:798-802`; Item4 `:139-181` related `Active` stays PropertyAccess |
| F5 owned-assign nit | **fixed** (documented) | `EffectLoweringPass.cs:765-768` PropertyAccess-only comment |
| Occupy-flat fail-before-mutate | **holds** | IfOnMutatedProperty + Item4 ModuleBook + exporter guard |
| Then-assigns must not leak into elseRhs | **holds in code; untested** | Independent copies from `priorAssignRhs`; F7 |
| PR 64 DEI RestoreActionState | **out of scope** | PR 65 diff has no DEI/Restore product hunks |
| Unique restore Unique-only | **unchanged** | `DomainEntityInstance.cs:601-602` |

Do not reopen F6 without a current-tree path where inner `if (OpenStays >= 1)` / else-if still probes pre-assign `OpenStays` (no Add in prefix) and Nights Failure leaves OpenStays=1.

---

## Process

- Nested/else-if inherit is a sibling of same-list subst, not “fresh dict = covered.” Empty then/else dicts were a contract miss on efa5ab7b; copy-in is the fix. Copy **from** `priorAssignRhs` into two new dictionaries so else cannot see then overlays.
- Prefer non-Unique Failure (Nights range) when claiming fail-before-mutate bag preservation, or pair Unique cases with ProbeCreate-before-assign (F3).
- Recurring class: order-only ProbeCreate tests do not lock taken-ness. F1/F2/F6 now pair subst-guard substring + non-Unique Failure + bag. F7 is the remaining taken-ness sibling (then overlay vs else).
