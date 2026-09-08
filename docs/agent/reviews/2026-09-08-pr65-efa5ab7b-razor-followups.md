# Razor follow-ups — PR 65 `efa5ab7b` — 2026-09-08

Source review: [`2026-09-08-pr65-efa5ab7b-razor.md`](2026-09-08-pr65-efa5ab7b-razor.md).

**Owner:** harden oracles / residual substitution scope. Do not implement inside the review branch as product “fixes while reviewing.” PR 65 product claim is shippable; F# below are coverage and latent-scope follow-ups.

---

## Open

- [ ] **F1** — `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:2388-2416` — Assert probe guard uses post-prior-assign substitution (`OpenStays + 1` / `OpenStays + 1L` before first `ProbeCreate` in Book). Add export sibling for AssignFalse expecting constant-false wrapped ProbeCreate (`if (false)`).

- [ ] **F2** — `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs:17-80` — Strengthen Item4 beyond order: InvokeAction nights=0 → Failure containing Nights, OpenStays==0, CreatedChildren empty, plus existing ProbeCreate-before-assign (or explicitly share/call the ActionEntityReturnTests oracle so Item4 cannot drift from simulate+print claim).

- [ ] **F3** — `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:702-737` — Harden ConditionDrift Unique test: assert module ProbeCreate-before-assign order (and/or use non-Unique illegal create) so Create/Occupied==0 cannot be Unique `RestoreActionState` theater.

- [ ] **F4** — `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:787-791` — When quantifier/relationship expression lowering is supported, stop substituting subject assign RHS into related-entity PropertyAccess inside quantifier bodies / relationship target props; add regression domain assign subject Active + `any rel where Active` + create.

- [ ] **F5** — (nit) `EffectLoweringPass.cs:763-766` — If owned/non-PropertyAccess assigns appear in guards, extend `assignedRhs` tracking or document fail-loud gap.

---

## Disposition / prior

| Item | Disposition | Evidence |
|------|-------------|----------|
| PR 64 DEI RestoreActionState | **out of scope** | PR 65 diff has no DEI/Restore product hunks; comments only |
| Occupy fail-before-mutate claim | **holds on tip** | Export `OpenStays+1>=1` ProbeCreate before assign; runtime Nights Failure + OpenStays==0; Restore Unique-only |
| AssignFalse not falsely taken | **holds on tip** | Export `if (false)` ProbeCreate; `AssignFalseSkipsCreate_Succeeds` |

---

## Process

- Prefer non-Unique Failure when claiming fail-before-mutate bag preservation, or pair Unique cases with ProbeCreate-before-assign order so restore cannot explain the oracle.
