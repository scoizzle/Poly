# Convergence plan v2: decisions V1 to V11 (answered)

## Scot's answers, 2026-10-03

V1 to V11 are all answered. V1–V9 and V11 as recommended; V10 as the revised scope below (an earlier answer rested on a false premise and was replaced). Standing approval in V11 does not release any wave (the release rule).

Companion to `pipeline-convergence-plan.md`. These were the only choices the five reviews and the code check left open. Everything else the reviews raised was either accepted into the plan or rejected with a reason. The eleven below are numbered **V1 to V11** so they do not collide with the older decision list (1 to 20) in PR 84. Three older ones (16, 18, 20) are still open; each is asked only when its lane reaches it. Decision 13 (function body form) and decision 19 (registering contributors) were answered 2026-10-09.

Each entry says what it is in plain English, the options, the recommendation, what it blocked, and the answer.

A note on numbering: the storage-only ruling is item 9 in PR 84's decision list ("what stays in the interpreter after DEI"). The same ruling is meant in both places.

Order: the first three unlocked the first waves of work. The rest wait until their slice is next.

---

## V1. Merge PRs 82 and 83 now?

**Answer:** done (PRs 82 and 83 merged; HP1 reached).

**What it is.** PR 82 and PR 83 had been approved to ship since Monday night. Ten slices depend on them directly (T1, B1, G1, K0, A3a, C0, C4a, C1a, C6a, K2) and most of the runtime and lowering work follows those, and they merge cleanly in either order. They have now merged (PR 82 squash `16a895dc`, PR 83 squash `945a2164`).

**Options.** (a) Merge both now. (b) Hold them while v2 is reviewed.

**Recommendation: (a).** Waiting only keeps both lanes idle after the doc-only wave 0.

**Blocks.** HP1: C4a, C0, T1, K0, B1, C1a, A3a and everything after them.

**If you do not answer.** Answered. HP1 is reached; later waves still start only on Scot's word (the release rule).

---

## V2. What does the interpreter's session storage offer? (the R2 conflict)

**Answer:** (a) minimal storage (save, get by id, list by type, remove; lookup by property value only if a test domain makes the scan too slow).

**The conflict.** v1 recommended a "generic piece inside the interpreter" with a link table, a uniqueness registry and notify callbacks (Decision 9, option a; slice C8a). Your ruling says the opposite: the interpreter keeps only session-aware storage, and uniqueness, links, constraints, transitions and notify must be trees. A first review flagged it as a contradiction, and that reading is right: PR 84's decision text already says your ruling wins, but section 1 and slice C8a still described the generic host. v2 removes that host. C8a is rewritten as C8a1 (storage only) and C8a2 (build an instance from a type definition). A structural test fails the build if storage ever contains a rule.

**What is left to decide.** Trees need to ask storage things like "is there another Reservation with this email?" (uniqueness) and "which instances watch this one?" (notify). What may storage answer?

**Options.**
- (a) **Minimal.** Per session: save an instance, get by id, list all instances of a type, remove. Trees do uniqueness by walking the list; links are ordinary reference fields on instances. Simplest and clearly rule-free. Cost: uniqueness is a linear scan in simulation.
- (b) **Minimal plus lookup by property value** ("find instances of type T where property P equals V"). Faster uniqueness, still no rule inside storage; the printed code can map it to a database query.
- (c) A link table inside storage. Rejected by your ruling; listed only so it is clear it was considered.

**Recommendation: (a), with (b) added only if a test domain makes the scan too slow.** It is the most literal reading of your rule and easiest to hand-edit.

**Blocks.** C8a1, which is wave 3. Nothing earlier.

**If you do not answer.** Answered. C8a1 proceeds with (a); lookup by property value only if a test domain makes the scan too slow.

---

## V3. How do hand-built domains meet the new gate?

**Answer:** (a) declare core types in fixtures.

**What it is.** With the "Compile refuses Errors" gate, 110 of 2895 existing tests fail (measured on `9db8868f`; the suite was re-run). 98 of those fail because a Domain built in code, in tests or in the MCP `oracle_expression` tool, never declares `Text`, `Number` and the other core types, so Analyze reports "unknown type". The DSL path adds them automatically; the code path does not. Today the simulator quietly runs on that error. After the gate it cannot.

**Options.**
- (a) **Declare them.** A shared test fixture (and the oracle tool) declares the core types. The gate stays strict. Anyone outside this repo who builds a `Domain` in code must declare them too.
- (b) **Analyze adds them for you** whenever a hand-built domain uses a core type. Existing hand-built code keeps working; the "unknown type Number" error disappears for code-built domains, and the two paths agree.
- (c) Only the simulator's `Create` entry point adds them. Rejected: a hidden rule in one place.

**Recommendation: (a).** It matches your "no hidden rules" stance and keeps Analyze's meaning the same on every path. Cost: a one-time edit to 11 test files, done in slice T1 with a command that can be re-run. If you pick (b), T1 becomes a small Analyze change instead.

**Blocks.** T1 and therefore G1, G2, G3 (the whole gate).

**If you do not answer.** Answered. T1 is written for (a).

---

## V4. Do entry/exit blocks and `when` handlers count as "named actions"?

**Answer:** (a) yes, entry/exit/when blocks count as named actions, but docs recommend authoring them as named actions (invoke an action; see M1).

**What it is.** Your rule: only named actions mutate their own entity's state; create, link and unlink are the one exception. Today an `entry { assign Title to "x" }` block, an exit block, or a `when` handler can assign the entity's own state and Analyze accepts it (a reviewer's probe). Cross-entity assignment is already a parse error. The new Analyze rule (M1) needs to know which side of the line these fall on.

**Options.**
- (a) **They count.** They are named behavior declared on the entity, so assigning its own state is allowed. The new Error fires only when a mutation targets another entity's state.
- (b) **They do not.** Only actions assign; entry, exit and handlers must invoke an action to change state. Stricter, and it breaks any authored domain that assigns in an entry or handler today.

**Recommendation: (a).** Entry, exit and handlers already run as part of the entity's own lifecycle and are visible in the model. (b) can be added later as a separate rule.

**Blocks.** M1 (wave 2) and the matrix T3.

**If you do not answer.** Answered. M1 is written for (a), with the authoring recommendation in the plan.

---

## V5. What is the source of the legal stage-transition table?

**Answer:** (a) derive the table from transition effects.

**What it is.** You ruled that transition tables compile as trees. The DSL has no transition declarations, so there is nothing yet to build the table from, and without a stated source nobody can write a test for it.

**Options.**
- (a) **Derive it.** A transition is legal if the model declares it through a `transition to X` effect: an action inside stage S that transitions to X gives S to X; an action not tied to a stage gives any stage to X. Anything else (for example an MCP call that forces a stage) is refused.
- (b) **Declare it.** New DSL syntax for allowed moves (for example `from Draft to Active`). Clearest to read; more authoring work and a parser change.
- (c) **No table.** Any stage to any stage; only unknown names are rejected. Contradicts your ruling.

**Recommendation: (a)**, and (b) later if you want hand-authored restrictions that the effects do not already imply.

**Blocks.** C4d (wave 2).

**If you do not answer.** Answered. C4d proceeds with (a).

---

## V6. What happens to an artifact id when an element is renamed?

**Answer:** (a) a rename is a new id; stands unless Scot overrides.

**What it is.** Ids are written name path plus type (settled). A review points out that evolution treats a rename as delete plus create, so anything that referenced the old id would dangle. Inside one compile this cannot happen, because the catalog is rebuilt whole and references are recomputed. It only matters for ids someone stores outside the compile.

**Options.** (a) **A rename is a new id.** Ids are derived by every compile; nothing stores them outside. (b) **A rename keeps the old id** through a recorded rename map in evolution.

**Recommendation: (a).** It is the direct consequence of "id = written name path". (b) adds a second identity system you would have to maintain by hand.

**Related, no decision needed.** Same-named actions in different stages are one method that dispatches on the current stage, so they are one artifact and the stage need not be in the id. That was checked in the exporter.

**Blocks.** Nothing hard. It fixes the wording of A1 and one dogfood probe.

**If you do not answer.** Answered. The plan uses (a).

---

## V7. An unbound contract endpoint: what do simulate and print do?

**Answer:** (a) both return the same Failure value.

**What it is.** When a domain calls an external endpoint that has no adapter, simulation returns a Failure ("has no in-process adapter on simulate") but the printed C# throws `NotImplementedException`. That breaks simulate equals print.

**Options.** (a) **Both return the same Failure value**; the printed code gets a default adapter that returns it. (b) **Both throw.** (c) **Analyze Error** when no adapter is bound. Not usable as is: simulation never has an in-process adapter, so every domain with an endpoint would fail to simulate.

**Recommendation: (a).** Fail as a value, same message, matching how the rest of the runtime reports blocked actions.

**Blocks.** C1c (wave 1, last part of the old C1) and therefore the end of the `BindForSimulate` removal.

**If you do not answer.** Answered. C1c proceeds with (a).

---

## V8. Should the equality constraint be writable in the DSL?

**Answer:** (a) make equality authorable (P1).

**What it is.** An equality constraint (a property must equal a value) exists in the model, the analyzer and the printed C#, but the parser can never create one and the DSL printer prints it as an empty string, so a domain built through the API loses it on a DSL round trip. The simulator also ignores it on create while the printed C# enforces it, which is why C4a fixes that early regardless of this decision.

**Options.** (a) **Make it authorable** (small parser and printer slice, P1, with a round-trip test). (b) **Remove it from the model.** (c) **Keep it API-only** and make the printer fail loudly instead of printing nothing.

**Recommendation: (a).** A rule that exists in the model but cannot be written or round-tripped is a hidden rule.

**Blocks.** P1 only.

**If you do not answer.** Answered. P1 is in the plan.

---

## V9. Owned or aggregate rules, and deleting instances

**Answer:** (a) analysis only for now, permanent entry on the H4 known-gaps list.

**What it is.** The model has owned/aggregate rules, but only the analyzer uses them; nothing enforces them at run time. Separately, it was checked: **there is no delete operation for instances anywhere in the product code or MCP tools.** A review asked what delete must do for owned children and for things that still point at the instance. That question has no code to apply to yet.

**Options.**
- (a) **Analysis only for now.** Record it as a permanent entry on the H4 known-gaps list with an owner ("when a delete operation is designed"). Unlink still gets its minimum-link tree (C6c).
- (b) **Enforce the owned-child rules on create and unlink now**, still without delete.
- (c) **Design delete now** (refuse if anything points at it, or cascade to owned children) and enforce it in trees.

**Recommendation: (a).** Do not design an operation that does not exist; make the gap visible so it cannot hide.

**Blocks.** H4's known-gaps file only.

**If you do not answer.** Answered. The plan uses (a).

---

## V10. Put the three 09-27 defects into this plan?

**Answer (Scot, 2026-10-03): settled, revised scope.** Q2 (MCP harness catalog for domains that use `uses sqlite` or `uses http`) stays as a slice. Q1a shrinks to a simulate-equals-print parity test for any/all/none/filtered count. Q1b is: regenerate `demo/Poly.RestApi` (the stale `Patron.cs`) and add a test that the checked-in demo equals fresh output. Reason for the revision: an earlier "yes" described defects that master had already fixed (quantifiers lowered in PR 79; the printer fails the export rather than printing a stub).

**What it is.** Three items from the 09-27 reviews had no slice: (1) collection rules (any/all/none/filtered count) — already lowered into foreach tree nodes in PR 79 (`DomainExpressionLoweringPass.LowerFilteredQuantifier`; `AnyRelated`/`AllRelated`/`CountRelated` are gone); residue is parity-test rows. (2) printed policies — the exporter already fails the whole export for a policy it cannot lower (no per-policy stub); the throwing `HasOverdueLoans` exists only in the stale checked-in `demo/Poly.RestApi/Patron.cs`. (3) the MCP harness cannot open a domain that says `uses sqlite` or `uses http`. They stay named in this plan, with (1) and (2) reduced as above.

**Options.** (a) **Yes, revised scope.** Q2 yes; Q1a only as parity-test rows for any/all/none/filtered count; Q1b only as regenerate `demo/Poly.RestApi` and test it equals fresh output. Wave 2. (b) **No**, keep them in a separate plan. (c) **Q1 now, Q2 later.**

**Recommendation: (a)** with the revised scope; this is the option taken. Q2 is what lets the harness hold the domain the compiler ships. Q1a and Q1b as originally written rested on a false premise; the revised scope is parity rows and demo regeneration.

**Blocks.** Q1a, Q1b, Q2 (now unblocked, still subject to the release rule). C5a no longer depends on Q1a (lowering is done).

---

## V11. Merge and review cadence for about 80 slices

**Answer:** (a) standing merge on SHIP + gate YES for the pure-deletion, rename, docs, test-only and unwired-new-code slices listed in V11; hand-merge the rest. Standing approval does not release any wave (the release rule).

**What it is.** The plan has 82 slices, so about 82 merges by you, and the verifying reviewer is the only verifier for both lanes. PR 82 took six verification-review rounds. 42 slices get one review pass and 40 get two (the exhaustive first review, then a verification review). N1 can change MCP-visible severity text and K6 changes Emit behavior; they are on the list as approved; Scot can take them off.

**Options.**
- (a) **Standing approval to merge** when the verifying reviewer says SHIP and the gate is YES, for the pure-deletion, rename, docs, test-only and unwired-new-code slices: T0, A1, A2a, A2b, K1, H1, N3, N1, C0, C3b, C7b, C7c, C7d, K3a, K3c, K6, F6, R1, R2. You hand-merge the rest, in particular anything that changes printed output or MCP behavior (C4b, C6a and others) and the risky ones (A3a, A5a, A5b, C1a to C1c, C3a, C5a, C5b, Q1a, C8 series, K2, K5).
- (b) Hand-merge everything.
- (c) Standing approval for every SHIP.

**Recommendation: (a).** It keeps your queue, not the mills, from setting the pace without giving up control of the slices that can change behavior.

**Blocks.** Nothing. It affects pace.

**If you do not answer.** Answered. Standing merge as (a) for the listed slices; you hand-merge the rest. Standing approval does not release any wave (the release rule).

---

## Scot's answers, 2026-10-09

### Decision 13. Function body form

**Answer (Scot, 2026-10-09 15:41 CDT):** a `DomainFunction` body is a single expression.

**What it is.** F1 adds `DomainFunction(Name, Parameters, ReturnType, Body)` on `Domain`. The body had to be one form before the record could be written.

**Blocks.** F1 (now unblocked).

---

### Decision 19. Registering contributors

**Answer (Scot, 2026-10-09 15:41 CDT):** yes, the database library registers its own DbContext artifact, with the database kind read from loaded libraries the same way Http registers `demo.http`.

**What it is.** N2 did the Http half: `HttpLibrary` registers `MinimalApiHostArtifactContributor`. The DbContext `AddArtifactContributor` in `DslCompiler.OpenCompileSession` waited on this answer. N2b is that remaining half.

**Blocks.** N2 (merged as Http half); N2b is the DbContext half.

---

### Ruling. Create-in back-reference slot (scope of decision 10)

**Answer (Scot, 2026-10-09 23:16 CDT):** KEEP. Decision 10 (no guessing) does not cover the create-in back-reference slot choice, so `FindAutoWireBackReference` and `TryLinkCreateInBackReference` stay as C6a leaves them. This is a ruling, not a new numbered decision.

## Parked, not asking now

- Decision 16 (`Information` to `Info`) before N1. Decision 18 (cascading errors) after N3's numbers. Decision 20 (rename scope) before R2.
- Pulling "tests and spec exports" (A7) forward for sellability: recommended not now, because nothing consumes them yet. The sellable-proof scenario (author, compile, run on sqlite, replay over `demo.http`) needs no code and runs after wave 1.

## What did not need to be asked

These look like decisions but were answered by earlier rulings or by the code: gating Errors everywhere (decision 7); no auto-link (decision 10); constraints on set, transitions as trees, unknown names as Analyze errors (decisions 11 and 12); action- and stage-scoped policies compile into the printed output (decision 8); same-named stage actions share one id (the exporter makes them one method).
