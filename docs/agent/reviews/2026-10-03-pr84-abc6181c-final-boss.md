# Final Boss review: PR 84 at abc6181c (re-verify, builds on 0974ad5b and d827f62d)

**Verdict: SHIP.** Bugs: 0. Open: F65, F66, F71 (suggestions); F67, F68, F69, F70 (nits).

- PR: scoizzle/Poly #84 into master (`9db8868f`). Head `abc6181c819d328822687aa5671205df57962cf2`, four commits: `0974ad5b` (Grok Build), `83d3731c` (Grok Build, fixes), `d827f62d` (hand edit, :65 wording), `abc6181c` (hand edit, one sentence under item 11).
- Implementer lineage: Grok Build, so the review mill is OpenCode `opencode-go/deepseek-v4.1-flash` (read-only prompt, `OPENCODE_DB` unset, first model, no fallback, exit 0, worktree clean). Log `/workspace/pr84b-oc.log`.
- Parked and not re-raised: F54, F55, F56, F58, F62, F63 (now closed), F64. F57 and F60 nits are unchanged.
- Earlier review: `2026-10-03-pr84-0974ad5b-final-boss.md` (not ship, F50-F52).

## Scope and byte checks
- `git diff origin/master..HEAD` touches only `docs/domain-modeling/pipeline-stage-map.md` and `pipeline-convergence-plan.md` (+618 lines). `git diff --check` is clean.
- Stage map sha256 `6025abca12d990f52a9007a52198e36e4d2abcae56628faccab026c3d5d52557` equals `/home/box/agent-data/projects/poly/pipeline-stage-map.md`. Unchanged across all four commits.
- Plan vs the agent-data copy: differences only at `:5` (pointer), `:65` (apply_dsl/Evolve sentence), `:92` (pointer), and section 7 (`:419-423` intro, HELD sentence and precedence note; `:426-433`, `:436-442`, `:445-449` item tails). No other hunk.
- Delta from d827f62d to abc6181c: exactly one line, `:439` (item 11), one sentence appended.

## Closed-or-open table
| Finding | Status | Evidence |
|---|---|---|
| F50 item 15 wording | **Closed** | `:433` now reads "No preference. After Scot signs off on this plan, non-conflicting slices start in the order Foreman picks. Until then A1, A2, H1, N1 and N3 stay HELD." Matches the relay. No internal contradiction with the HELD sentence at `:421`. |
| F51 HELD contradicted | **Closed** | Precedence note `:423` says HELD overrides "can start now"/"could run" and names the three sites (H1 slice text `:276`, conflict table `:295`, Wave 0 `:304`), and says N1 also waits on open item 16 (`:447` is open, "Before N1"). The lines are deliberately unedited. Residual gap: F71. |
| F52 Decision 9 vs C8a | **Closed** | Item 9 (`:437`) now says it overrides C8a and the section 1 storage assumption (`:94`, `:170`, `:175`) and that the text needs rewriting before C8a starts. Redundant wording: F70. |
| F53 decided questions read open | **Closed** | `:423` maps C4c to item 11, C4d to 12, C6 to 10, H2 to 14 and says the decided items win. Checked against "Before C4c/C4d/C6/H2" in items 11, 12, 10, 14. |
| F59 :65 wrong for apply_dsl | **Closed, verified in code** | See below. |
| F61 raw files dangling | **Closed for the plan, residual** | `:5` and `:92` say "(kept outside the repo)". `:5` still says "next to this file" (F69). `stage-map:169` is unchanged and still says "next to this file" (cannot be edited without breaking byte-identity; carry to K1 with F54). |
| F63 mutation exception question | **Closed** | `:439` answers it: create, link and unlink are the one named exception; the relationship owns them. Residuals F65-F68. |

## Check (c): `:65` against current master
- `Poly.Mcp/Tools/DomainTools.cs:1315-1328`: `apply_dsl`'s `if (!outcome.Succeeded)` branch returns a failure response. `McpSessionStore.Replace` is only reached at `:1331`. So a failed `apply_dsl` stores nothing. **Correct.** (Foreman's range 1322-1329 is a few lines off; the return is `:1322-1327`.)
- `Poly.Mcp/Sessions/McpSessionStore.cs:90-93`: on `!outcome.Succeeded`, `Sessions[sessionId] = current with { LatestAnalysis = outcome.Analysis }` keeps the old domain and stores the error analysis. **Correct.**
- The only caller of `McpSessionStore.Evolve` is the private helper `DomainTools.Evolve` (`:938`, call `:953`), used by the Add and Remove cases (`:526-886`). The doc's phrase "tools that edit through the shared `Evolve` helper in `DomainTools.cs`" is right.
- Export pairs them: `OracleTool.cs:119-123` checks only that an analysis exists, then emits from domain plus analysis. **Correct.**
- Other writers in Poly.Mcp (mill list, spot-checked): `Create` (`DomainTools.cs:151`), `Replace` (`:1331`), `Remove` (no caller), and `TryModifyInstances` (instance maps only, `RuntimeTool` create, link, unlink, invoke).

## Check (d): contradictions introduced by the edits
- Item 15 vs HELD (`:421`, `:423`): consistent.
- Precedence note vs item text: the cross-references resolve (items 11, 12, 10, 14 for C4c, C4d, C6, H2; item 16 for N1; "Decision N" equals item number everywhere). Numbering of items 1-20 is unchanged.
- New `:439` sentence vs item 11's other text, the HELD note and the slice text: **no hard contradiction** (details F65-F68). It matches the code shape: `create in Rel { ... }` is a language effect (`CreateEntityInRelationshipEffect.cs`), and link and unlink are store and tool operations (`DomainInstanceStore.cs:364,379`, MCP `link_instances` and `unlink_instances`). It does not conflict with C8c (a move of the tools) or with Decision 10 (links come from the model and trees enforce them).

## Hand-editability gate: YES, with one suggestion (F71)
Scot can open both files cold and change them by hand. The text is plain English. The only cross-section rule is explicit and sits directly under the HELD sentence, with the three affected sites named. Leaving the "can start now" lines unedited and overridden by a note is **acceptable for a draft that is waiting for sign-off**: the edit is deferred until the slice text is rewritten, and an agent-free editor finds the rule by reading section 7 first. The risk is a reader who stops at `:276` or `:295`. A one-line pointer in section 0 ("read section 7 first: decided items and HELD override earlier text") closes it (F71). Code claims spot-checked (about 45 across both reviews) match master apart from the earlier F60 nits.

## New findings (F65 onward)
| # | Sev | Where | Finding |
|---|---|---|---|
| F65 | suggestion | `plan:439` | The exception does not say create, link and unlink still enforce every invariant (cardinality, constraints on the created instance). The sentence before it and item 10 imply yes, but "the relationship owns those lifecycle effects (think RAII)" can be read as moving enforcement away. Add "and they still enforce every invariant". |
| F66 | suggestion | `plan:439` | "they are the only time an outside entity can influence another entity's state" is too strong. Cross-entity `invoke Rel.Action` (`Poly.Mcp/Docs/poly-dsl-guide.md` ~518-528; runtime test `McpSmokeTests.cs:2813` `ApplyDsl_CrossEntityInvoke_RuntimePasses`; `InvokeActionEffect.cs`, `ForEachInvokeEffect.cs`) lets one entity cause another's named action to run. That is consistent with the rule (the target's own named action mutates it) but it is also an outside influence. Say "the only time an outside entity can change another entity's state directly". |
| F67 | nit | `plan:439` | "effects of relationship operations": create is a language effect, but link and unlink are store and tool operations with no effect form today (`poly-dsl-guide.md` ~990; `DomainInstanceStore.cs:364,379`). Under Decision 10 they need a tree form in C6. Say "relationship operations" or note that. |
| F68 | nit | `plan:439` | MCP `create_instance` of a root entity (`RuntimeTool.cs:148,197`) is not a relationship operation, so it is an unnamed mutation path (session storage, Decision 9). One phrase settles it. The new sentence also has no slice cross-reference (C6, C8c), unlike item 9. |
| F69 | nit | `plan:5`, `stage-map:169` | `plan:5` says "next to this file (kept outside the repo)", which contradicts itself. `stage-map:169` still says "next to this file" with no outside-the-repo note, and cannot be changed without breaking byte-identity. |
| F70 | nit | `plan:437`, `:439` | The "Decided 2026-10-02" paragraphs for items 9 and 11 mix Scot's recorded words with editorial text (item 9 repeats its decision twice). A hand editor cannot tell what Scot said from what the plan added. Put the override in a separate sentence tagged "Plan note:". |
| F71 | suggestion | `plan:295-296`, `:423` | The note lists three "can start now" sites, but row `:295` also says A3 to A5 and F1 to F2 can start now (A3 depends on held A1 and A2), and row `:296` (A5, C9, N2) says "Can start now". Neither is HELD, but both transitively wait on held slices. Widen the note to dependents of HELD slices and add the section 0 pointer. |

## Mill disposition
Mill produced 15 findings. I downgraded its two "bug"s: the "effects" wording (F67) and root `create_instance` (F68) are nits, not bugs, because the sentence is about relationship operations and cannot be read as a code claim about Effect IR. Its item 11 "invariant enforcement" point is F65. Its "only time" point is F66 and was confirmed against the guide and the runtime test. Its `plan:5` "bug" is a nit (F69). Its writer list matched my own grep of `McpSessionStore` call sites. Its item 15 "No preference then a process" nit is dropped: it follows the relay wording Foreman specified. Its item 5 point on the "non-conflicting" set is folded into F71.
