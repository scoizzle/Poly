# PR 62 Warden Follow-ups — 2026-09-08

- **Review**: [`2026-09-08-pr62-d6e19721-warden.md`](2026-09-08-pr62-d6e19721-warden.md)
- **PR**: [#62](https://github.com/scoizzle/Poly/pull/62) (SHA `d6e197219e4aa18389d624170f325865b5fb8a7a`)
- **Open bugs**: none

## Follow-up Tasks

- [ ] **F1** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:675` — Rephrase comment "same seam as CreateNav/Attach" to reflect that Create is now the single source of truth and CreateNav defers to it (suggestion)
- [ ] **F2** — `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:702` — Extract or annotate the inline inverse-count loop to stay in sync with `FindInverseCollection` (suggestion, maintenance risk)
- [ ] **F3** — `docs/CORE.md:185` — Split the appended sentence into a separate bullet for readability (nit)

## Prior Follow-ups

None — this is the first review of PR 62.
