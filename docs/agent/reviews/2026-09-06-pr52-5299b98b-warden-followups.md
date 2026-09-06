# Follow-ups — PR 52 Fine Type-create / Type+Rel (Warden) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr52-5299b98b-warden.md`
- Target: PR 52 SHA `5299b98bffd35dcd1311651e149540188f6bc410` vs `origin/master`
- Mode: standard

## Open bugs (must close before ship)

- [ ] **F1** — Place the fixtures on the live probe tree and enroll consumers. File: `probes/dogfood/simulate-create-type.poly:1` (and `simulate-create-in.poly`, `simulate-create-create-in.poly`). Do: move to `docs/probes/dogfood/`; add `DslCompilerCompileOracleTests` Arguments; list them in `docs/probes/README.md`. Do not leave a `probes/` tree that `66f8eeb0` retired. A merge that only adds unreachable files does not close Fine/Type+Rel skew.

- [ ] **F2** — Add a fail-closed MCP simulate test for the contract the comments describe. File: `probes/dogfood/simulate-create-create-in.poly:5-8`. Do: TUnit via `DslTool.ApplyDsl` + `RuntimeTool.CreateInstance` + `InvokeAction` + `ListInstances` + `PolicyTool.EvaluatePolicy` + store `GetRelatedInstances("fines"|"patron")` + `returnInstanceId`. Assert Type-only (list Fine=1, HasFines false, fines links=0, patron reverse=0), Rel-only (list=1, HasFines true, both directions linked), and **combined sequential Type then Rel on one Patron** (list=2, fines links=1, reverse counts 0 and 1). Compile-check is the wrong rung.

## Suggestions

- [ ] **F3** — Stop claiming a product close this SHA does not contain. File: `probes/dogfood/simulate-create-type.poly:6-9`. Do: retitle/body as chore/discovery **or** land the actual HostAbi/guide change with F2 as the failing test first. Type-create non-link is `poly-dsl-guide.md` §0.3–0.4 and this probe’s own Expect line. Reverse `patron` on create-in is **not** soft at `DomainEntityInstance.HostAbi.cs:642-651` (verified MCP on this SHA). “Off master, does not mix PR 51 product” means this PR cannot close a runtime bug.

## Process

- [ ] **F4** — Gate live-probe placement. Recurring class: fixtures added under repo-root `probes/` after `docs/probes/` became the consumer tree (`66f8eeb0`, `docs/probes/README.md`, `scripts/discovery-round.sh`, dogfood tests). Do: review/protocol or a cheap test that every committed `*.poly` probe is under `docs/probes/` (or is explicitly historical under `docs/plans/archive/probes-2026-08/`). Do not accept a “fix” PR whose only delta is an unreferenced `.poly`.

## Nits

None filed (bugs present).

## Disposition of prior items

No prior PR 52 review files. PR 51 razor notes under `docs/agent/reviews/2026-09-05-pr51-48a92220-razor*.md` are a different target and were not re-opened here.
