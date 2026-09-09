# PR 62 Final Boss Follow-ups — 2026-09-09

- **Review**: [`2026-09-09-pr62-f02bdcb4-final-boss.md`](2026-09-09-pr62-f02bdcb4-final-boss.md)
- **PR**: [#62](https://github.com/scoizzle/Poly/pull/62) (SHA `f02bdcb44606388db86b5c3749d8dbdfa23e0ade`)
- **Mode**: re-verify of Warden `docs/agent/reviews/2026-09-08-pr62-d6e19721-warden-followups.md`
- **Model**: grok-4.6
- **Open bugs**: 1 (F1) — do not ship until closed

## Follow-up Tasks

- [ ] **F1** — **bug** — Named create-in / CreateNav / BindCreateIn fail closed when the parent has two collections of the child type. Files: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:726-732`, `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:138-141`, `:199-253`. Do: keep public `Child.Create(peer)` fail-closed **or** HostAbi skip, but `create in primary` must still add the child to **that** collection (runtime `Store.CreateIn` + `HostAbi.TryLinkInverseCollection` skip at `HostAbi.cs:905-915` already does). CreateNav fallback Add at `Notify.cs:227-237` is dead today because Create returns Failure first. Add a test that forces this sibling (Ambiguous DSL + `create in primary` / BindCreateIn): success, primary populated, secondary empty, back-ref set. Do not use `Export_PublicCreate_CheckIn` as coverage.

- [ ] **F2** — **suggestion** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:675-677` — Rephrase “same seam as CreateNav/Attach” so Create is the unique-inverse attach source and CreateNav/BindCreate defer (Warden F1, still open).

- [ ] **F3** — **suggestion** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:702-719` — Share one inverse-count helper with `FindInverseCollection` (`Actions.cs:868-880`) (Warden F2, still open).

- [ ] **F4** — **suggestion** — `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs` — Assert unique-path CreateNav defer: Guest.CreateReservations / Patron.CreateFines body contains `Target.Create` and does not contain `_reservations.Add` / `_fines.Add`. Separate from F1.

- [ ] **F5** — **nit** — `docs/CORE.md:185` — Split the appended public-Create sentence out of the lowering paragraph (Warden F3, still open).

## Process

- [ ] **P1** — Sibling-path checklist for create/create-in reviews must include **named create-in when Type-create is ambiguous** (two many-navs to the same child + `create in Rel`). Warden at `d6e19721` signed “no sibling-path drift” after checking only unique-inverse BindCreate/CreateNav. Recurring class: unique-path fix; several-match sibling untested.

## Prior Follow-ups (Warden `d6e19721`)

- **Warden F1** (comment “same seam as CreateNav/Attach”) — **still open** → this file **F2**.
- **Warden F2** (inline inverse loop vs `FindInverseCollection`) — **still open** → this file **F3**.
- **Warden F3** (CORE.md paragraph) — **still open** → this file **F5**.
- Warden “open bugs: none” — **invalid**. Issue 1 / **F1** is a bug on this SHA; product files are unchanged from Warden tip (`d6e19721...HEAD` is GrammarBuilder docs only).
