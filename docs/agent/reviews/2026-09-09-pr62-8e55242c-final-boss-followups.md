# PR 62 Final Boss Follow-ups — 2026-09-09

- **Review**: [`2026-09-09-pr62-8e55242c-final-boss.md`](2026-09-09-pr62-8e55242c-final-boss.md)
- **PR**: [#62](https://github.com/scoizzle/Poly/pull/62) (SHA `8e55242c2cac2750915dac60c82e258b6e38ff0c`)
- **Mode**: re-verify of Final Boss `docs/agent/reviews/2026-09-09-pr62-f02bdcb4-final-boss-followups.md` (not ship)
- **Model**: grok-4.6
- **Open bugs**: none — ship

## Follow-up Tasks

None required to ship.

## Process

- [ ] **P1** — Standing sibling-path checklist for create/create-in reviews: include **named create-in when Type-create is ambiguous** (two many-navs to the same child + `create in Rel`). This SHA closed the product hole (`CreateInPrimary`). Keep the checklist so unique-path-only reviews do not recur.

## Prior Follow-ups (`f02bdcb4` Final Boss)

- **F1** (bug, named create-in fail-closed) — **fixed**. `DomainToCSharpExporter.cs:705-714` skip Attach when `FindInverseCollectionInfo.Unique` is null; `Notify.cs:228-233` fallback `_field.Add`; `Export_CreateNav_AmbiguousInverse_CreateInPrimary_Succeeds` (`Tests.cs:2944-3054`) runtime primary=1 / secondary=0 / back-ref set. BindCreateIn remains `this.Create{Rel}` (`StoreBind.cs:193-196`, unchanged vs `f02bdcb4`).
- **F2** (comment “same seam as CreateNav/Attach”) — **fixed**. `DomainToCSharpExporter.cs:675-680`.
- **F3** (share inverse-count helper) — **fixed**. `FindInverseCollectionInfo` `Actions.cs:869-891`; Create and CreateNav both call it.
- **F4** (unique CreateNav defer test) — **fixed**. `CreateFines` `Tests.cs:2045-2060`; `CreateReservations` `:2781-2796`.
- **F5** (CORE.md sentence / fail-closed wording) — **fixed**. `docs/CORE.md:185` own sentence; skip Attach not fail-closed.
- **P1** (process, unique-path-only sibling miss) — **still open as process**. Product F1 closed; checklist remains.

Do not treat `docs/agent/reviews/2026-09-09-pr62-f02bdcb4-final-boss.md` as current. That file is the not-ship baseline for this re-verify.
