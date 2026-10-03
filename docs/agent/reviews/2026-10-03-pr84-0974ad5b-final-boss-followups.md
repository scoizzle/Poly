# PR #84 Final Boss follow-ups (head 0974ad5b): verdict NOT SHIP

Fix round: the three bugs (F50, F51, F52) can all be fixed **inside section 7 of the plan**, so the stage map stays byte-identical and the plan still differs from the source only in section 7. Everything else is a later sweep.

## Blocking (fix before merge)

### F50 (bug): Decision 15 records words Scot is not known to have said
- **Where:** `docs/domain-modeling/pipeline-convergence-plan.md:431`.
- **Text:** "Decided 2026-10-02: Scot has no preference. Start non-conflicting slices; Foreman picks the order. A1, A2, H1, N1 and N3 are HELD until Scot signs off on this plan."
- **Why it is wrong:**
  - The relayed decision is only "no preference".
  - "Start non-conflicting slices" is an instruction Scot is not recorded as giving, and it contradicts the HELD sentence in the same item.
  - "Foreman picks the order" lets Foreman start HELD slices.
- **Suggested text:** "Decided 2026-10-02: Scot has no preference; Foreman decides the order. A1, A2, H1, N1 and N3 are HELD until Scot signs off on this plan."
- **Check before editing:** confirm the exact wording with Chieftan. The primary source was not on the box, so this finding rests on the relay and the internal contradiction.

### F51 (bug): the HELD marker is contradicted elsewhere
- **Where:**
  - `plan:276` (H1: "Can start now.")
  - `plan:295` (table row for A1-A5, H1, F1-F2, N1, N3: "**Can start now**")
  - `plan:304` (Wave 0: "A1, A2, H1, N1 and N3 … could run while 82 and 83 wait")
  - `plan:445` (Decision 16 "Before N1" is open, so N1 cannot start in any case).
- **Fix inside section 7:** add one sentence to the intro: "Where sections 1-6 say a slice can start now, the HELD marker wins for A1, A2, H1, N1 and N3. N1 also waits on item 16."
- **Fix later in the body:** change the three "can start" lines.

### F52 (bug): Decision 9 contradicts the plan's stated assumption and C8a
- **Where:** `plan:94` ("The slices below assume the first reading" = a generic link/uniqueness/VM-callback piece inside the interpreter), `plan:170` (C8a: "plus the generic link, uniqueness and notify host (Decision 9)"), `plan:175` ("the new generic host is the one place where a hand edit needs care").
- **Scot's decision (item 9, quoted faithfully):** the interpreter keeps only session-aware storage. Uniqueness, links, constraints and transitions must be trees. That is closer to option (b), the one the plan said is "a much bigger piece of work".
- **Why it blocks:** the plan names this as the largest design risk (C8), and the slice as written would build the host Scot ruled out. The `(Decision 9)` tag points at a decision that now says the opposite.
- **Fix inside section 7:** under item 9, add: "This is closer to option (b). The slices in section 1 that assume (a) (D2 text, C8a) need rewriting before C8a starts; the decision wins."
- **Fix later in the body:** rewrite `:94`, `:170` and `:175`. C8a becomes "session-aware storage plus the type factory"; uniqueness, links and notify become tree work under C4b/C5/C6.

## Non-blocking (queue, in priority order)

- **F53 (suggestion):** decided questions still read as open.
  - Where: `plan:148` (C4c "only if you choose so in Decision 11 and 12"), `:149` (C4d "only if Decision 12 says yes; otherwise… Analyze error"; Decision 12 decided both), `:158-159` (C6 "Either compile real link checks or make linking explicit… depends on the decision"; Decision 10 decided both), `:278` (H2 "same system or separate report"; Decision 14 chose same system plus a separate result set).
  - Fix: switch to decided tense.
- **F54 (suggestion):** the stage map is stale against recorded decisions.
  - Where: `stage-map:66` ("exactly two things"; Decision 4 says three), `:88` and `:130` (artifact analysis shape "decide later"; Decision 14), `:124` (id scheme "domain element id plus type"; Decision 1 says name path plus type), `:147` (generators "walk Domain instead of artifacts" is listed as a violation; Decision 5 makes them part of Compile).
  - Fix: slice K1 or a docs pass after Scot signs off. Do not edit now, or byte-identity with the source breaks.
- **F55 (suggestion):** status headers are stale.
  - Where: `plan:1`, `:3` ("no PR exists… nothing on the board changed"), `:7` ("none starts until you say so"), `:419`; `stage-map:1`, `:3` ("Not a PR").
  - Fix: one line saying the doc lives in PR 84, and keep "DRAFT" only until Scot signs off.
- **F56 (suggestion):** Decision 17 is not wired in.
  - Where: `plan:128` and `:219` still say "interim"/"gone or justified"; option (a) text says "revisit after C8".
  - Fix: name the slice that removes `RuntimeEnumTypeProvider` (C4c or C4d) and add it to C10's check.
- **F58 (suggestion):** section 7 grouping.
  - Where: `plan:423-446` and `:304`.
  - Fix: items are listed out of numeric order (1-7, 15, 8-12, 14, 17, 13, 20, 16, 18, 19).
    - Move 16 and 18 to the wave where N1/N3 sit.
    - Move 17 to "before merging 82".
    - Make "wave 0 and 1" consistent with "Wave 1 decisions".
- **F59 (suggestion):** wrong code claim at `plan:65`.
  - "A failed `apply_dsl` stores the error analysis next to the old domain" is wrong for `apply_dsl`: it returns before `McpSessionStore.Replace` (`Poly.Mcp/Tools/DomainTools.cs:1322-1329`).
  - The pairing is in `McpSessionStore.Evolve` (`Poly.Mcp/Sessions/McpSessionStore.cs:91-93`), used by other tools.
  - Fix: name `Evolve` and its callers, and check whether G2's optional follow-up is still needed.
- **F61 (suggestion; DOES-NOT-BLOCK):** three raw working files and `/workspace/poly-ro` are named but not in the repo.
  - Where: `stage-map:169`, `plan:5`, `plan:92`.
  - Fix: delete the pointers, or say they are kept outside the repo, or commit them.
- **F62 (suggestion):** order and graph.
  - Recommended order lists K3 (`:315`) before C2 (`:316`), but K3 depends on C2 (`:199`); the graph is missing K3's C0 edge (`:360-372`) and has a C8→C10 edge that the C10 text ("Depends on C1 to C3") does not state.
- **F57 (nit):** recorded-wording gaps.
  - Item 1 leaves the same-name-action/stage sub-question unanswered; record it as still open.
  - Item 3 drops the scaffolding tree that A3 relies on.
  - Item 11's extra sentence "Constraints propagate and are validated as early as possible" is UNVERIFIED against Scot's words.
- **F60 (nit):** counts and line slips.
  - `plan:28` "2 test files touch the catalog" (1 by name).
  - `plan:198` EffectLoweringPass "about 27" is a doc comment; the real fallback is `:1037`.
  - `plan:254` N1 "9 identifier hits" (13 lines in product code).
  - `plan:332` R2's parenthesis "(loose: includes `ToLowerInvariant`)" is backwards, and its totals use a different method from the stage map's 225/27.
  - `plan:186`/`:205` "five side tables" counts `Module`.
- **F63 (nit):** how direct MCP mutations (`create_instance`, `link_instances`, `unlink_instances`, moved in C8c) fit "only named actions mutate their own entity" is not stated. A one-line answer from Scot is enough.
- **F64 (nit):** first-person voice to Scot ("I spot-checked", "needs from you") in a repo doc.

## Carried
No code findings are carried (docs-only PR). The older F-ids for PR 82 (F46, F47, F42, F44, F7, F41, F4 remainder, F40) are unrelated to this PR.
