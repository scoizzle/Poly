# Final Boss follow-ups — PR 65 `efa5ab7b` — 2026-09-08

Source review: [`2026-09-08-pr65-efa5ab7b-final-boss.md`](2026-09-08-pr65-efa5ab7b-final-boss.md).

**Owner:** close F6 (nested/else-if `assignedRhs` inherit) before ship. Do not implement inside the review branch as product “fixes while reviewing.” Razor F1–F5 remain coverage / latent-scope; they do not block once F6 is closed with a Nights oracle.

---

## Open bugs (must close before ship)

- [ ] **F6** — `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:804-805` and `:816-817` — Copy `priorAssignRhs` into a **new** dictionary for then-walk and a **new** dictionary for else-walk so inner `if` / `else if` conditions see ancestor sibling assigns, while then-assigns do not leak into else. Do **not** share one mutable dict across then and else. Add tests (InvokeAction + export or module FlattenSyntax):
  - nested: `assign OpenStays to OpenStays + 1` then `if (confirm) { if (OpenStays >= 1) { create Stay { Nights: nights } } }` with confirm=true, nights=0 → `Succeeded==false`, ErrorMessage contains Nights, **OpenStays==0**, empty `CreatedChildren`, ProbeCreate before OpenStays assign, inner probe guard contains `OpenStays + 1` / `OpenStays + 1L`.
  - else-if: same assign then `if (confirm) { } else if (OpenStays >= 1) { create Stay { Nights: nights } }` with confirm=false, nights=0 → same bag+order+guard assertions.
  Use Nights range Failure, not Unique (Unique still hits `RestoreActionState` at `DomainEntityInstance.cs:601-602`).

---

## Open suggestions

- [ ] **F1** — `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:2388-2416` — Assert probe guard uses post-prior-assign substitution (`OpenStays + 1` / `OpenStays + 1L` before first `ProbeCreate` in Book). Add export sibling for AssignFalse expecting constant-false wrapped ProbeCreate (`if (false)`).

- [ ] **F2** — `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs:18-80` — Strengthen Item4 beyond order: InvokeAction nights=0 → Failure containing Nights, OpenStays==0, CreatedChildren empty, plus existing ProbeCreate-before-assign (or share the ActionEntityReturnTests oracle).

- [ ] **F3** — `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:702-737` — Harden ConditionDrift Unique test: assert module ProbeCreate-before-assign order (and/or use non-Unique illegal create) so Create/Occupied==0 cannot be Unique `RestoreActionState` theater.

- [ ] **F4** — `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:787-791` — Stop substituting subject assign RHS into related-entity PropertyAccess inside quantifier bodies / relationship target props. Named-action/export `any` still throws (`DomainExpressionLoweringPass.cs:336-339` when `UseThisReference`); entry/exit VM and subscriptions lower `StoreQuantifier` (`UseThisReference` false). Add regression: assign subject Active + `any rel where Active` + create, preferably OnEntry so the VM path lowers.

- [ ] **F5** — (nit) `EffectLoweringPass.cs:763-766` — If owned/non-PropertyAccess assigns appear in guards, extend `assignedRhs` tracking or document fail-loud gap.

---

## Disposition / prior (Razor F1–F5 + Occupy claims)

| Item | Disposition | Evidence this SHA |
|------|-------------|-------------------|
| Razor F1 export guard not locked | **still open** (suggestion) | Exporter test `:2410-2415` order only; dump `if (this.OpenStays + 1L >= 1L)` exists but suite does not assert it |
| Razor F2 Item4 order-only | **still open** (suggestion) | Item4 `:18-80` order only; runtime oracle is ActionEntityReturnTests `:1194-1235` |
| Razor F3 ConditionDrift Unique theater | **still open** (suggestion) | `:702-737` Unique + Restore `:601-602`; no ProbeCreate-before-assign |
| Razor F4 quantifier name-subst | **still open** (suggestion); Razor “VM throws” **narrowed** | Export/named-action throw; entry/exit VM `StoreQuantifier` |
| Razor F5 owned assign | **still open** (nit) | `:763-766` PropertyAccess-only |
| PR 64 DEI RestoreActionState | **out of scope** | PR 65 diff has no DEI/Restore product hunks |
| Occupy-flat fail-before-mutate | **holds** | Export `OpenStays+1>=1` ProbeCreate before assign; InvokeAction Nights + OpenStays==0; Restore Unique-only |
| AssignFalse not falsely taken | **holds** | Export `if (false)` ProbeCreate; `AssignFalseSkipsCreate_Succeeds` `:740` |
| Nested / else-if inherit | **new bug F6** | Empty then/else dict `:805` `:817`; runtime OpenStays=1 on Nights Failure |

---

## Process

- Prefer non-Unique Failure when claiming fail-before-mutate bag preservation, or pair Unique cases with ProbeCreate-before-assign order so restore cannot explain the oracle.
- Sibling-path for substitution: same-list prior assigns **and** ancestor assigns visible to nested/else-if probe conditions (copy-in, not empty dict). Razor listing “fresh dict” as coverage without a Nights nested oracle missed F6.
- Recurring class: order-only ProbeCreate tests do not lock taken-ness. If F1/F2/F6 recur, require subst-guard substring + non-Unique Failure + bag in the same test.
