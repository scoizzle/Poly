# Long-term growth review — Poly platform

**Date:** 2026-08-08  
**Mode:** full-project adversarial review (not a single PR).  
**Lens:** correctness contracts **and** multi-year growth (complexity, duals, ownership, operability).  
**Evidence baseline:** current tree on `rewrite/domainmodeling-from-scratch` (ahead of origin; includes uncommitted complexity-map / grammar-revision docs).  
**Maps used:** [`docs/CORE.md`](../../CORE.md), [`docs/complexity-semantic-map.md`](../../complexity-semantic-map.md), [`docs/plans/dead-dual-inventory-2026-08-08.md`](../../plans/dead-dual-inventory-2026-08-08.md), live greps/LOC.

**Verdict:** The spine is real and shippable. Growth risk is **not** “no architecture” — it is **unbounded surface area around a correct core**, plus **process/docs inflation**, plus a few **god-modules** that will resist every next feature. Without a deliberate “one dual at a time” policy, agents will keep adding bags, changes, and suites until the project feels unmaintainable even when green.

---

## 0. Executive summary

| Dimension | Assessment |
|-----------|------------|
| **Architectural spine** | Strong: Domain → evolution gate → DE lower → Ast → analyze → VM |
| **Product honesty** | Improving (MCP minify, DSL guide, fail-closed culture) but pure-Grammar **claims** still outrun Option A reality |
| **Test wall** | ~1840 `[Test]` methods, ~41k test LOC — excellent safety net, expensive to move types |
| **Dead weight** | Validation + Text.Matching (and most of Text) are paid every compile and every mental model |
| **Growth choke points** | `DirectVmAbiEmitter` (~3k), `DomainEntityInstance` (~1.4k), `EffectAnalyzer` (~1.2k), `DomainChange` (~1.2k / 40+ types), domain analysis bag count |
| **5-year risk** | Becoming a second enterprise modeling suite with two of everything, not a tight neurosymbolic product |

**Strategic recommendation:** Treat the next 2–4 quarters as **stabilization + dual reduction**, not greenfield capability. Admit product suites only when they close a demon or fund a dogfood path.

---

## 1. What is working (do not “fix”)

These are long-term **assets** — growth should deepen them, not replace them:

1. **Immutable domain + evolution gate** — single mutation story; rollback is real.
2. **DomainExpression as domain IR, Ast as program IR** — the dual is intentional and correct for a neurosymbolic stack; do not merge the types.
3. **VM as canonical execution** — ADR held; LINQ as oracle is honest.
4. **Fail-closed culture** — parse/analyze/runtime layers; empty/missing often throws.
5. **Catalog + capability + subscription plans** — product runtime facts have a path.
6. **MCP thin adapter + unified add/remove + DSL-only expressions** — catalog diet was the right growth move.
7. **Grammar as pattern table** — right engine shape; RuleRef/LeftAssoc complete the recursive/left-assoc story.
8. **Pack extension seams** — annotations, expression forms, PassRegistry (extension without ABI forks).
9. **Status monopath + complexity map** — process anti-demons now exist; use them.

---

## 2. Issues (long-term growth)

Severity here is growth-oriented:

- **bug** — will cause wrong behavior or silent drift as the system grows  
- **suggestion** — structural debt that multiplies cost of every feature  
- **nit** — hygiene / clarity that still matters for agents and onboarding  

### Issue G1 — Severity: suggestion (structural)

- **Area:** `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (~1387 LOC)
- **Failure mode over time:** Every new effect, quantifier rewrite, peer binder, or invoke rule lands in one type. Review cost → ∞; accidental coupling between policy preprocess, VM effects, and store mutations.
- **Evidence:** Invoke pipeline, dual effect strategies (VM-compiled vs direct), peer binding rewrites, create-in, depth guards, type-def analyzers all colocated (see summary comments ~196–210, `EffectExecutor`, quantifier rewrite fields).
- **Growth fix:** Extract **EffectRunner**, **PolicyPreprocessor**, **InstanceGraphMutations** as collaborators owned by Runtime. Keep `DomainEntityInstance` as state + façade. No behavior change first.

### Issue G2 — Severity: suggestion (structural)

- **Area:** `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` (~2998 LOC)
- **Failure mode:** Domain pressure to special-case the emitter (CORE forbids this, but file size invites it). Diffs become unreviewable; EH/lambda/member paths rot unevenly.
- **Evidence:** Largest file in core; many `NotSupportedException` arms for incomplete Ast coverage.
- **Growth fix:** Split by **node family** (expr / stmt / type / lambda) only when a feature forces a file touch — not a vanity rewrite. Hard rule: **domain never adds cases here**; lower/replace upstream.

### Issue G3 — Severity: suggestion (dual / product honesty)

- **Area:** Expression language — `DslExpressionParser` Option A ladder vs `DslGrammar` LeftAssoc span tables; `DomainDslPrinter` vs Grammar `Printer`
- **Failure mode:** Agents and docs claim “pure Grammar”; implementers maintain **two languages** (fold vs span) and **two printers**. Silent precedence/span drift (already pinned once for `not`-in-chain).
- **Evidence:** gpure follow-ups S1/S5; complexity map D4/D5; `SpanVsFold_NotInChain_TableRejectsFoldAccepts`.
- **Growth fix:** Either **grammar wrap-up** (live fold from LeftAssoc) or **delete unused span consumer ambition** and reword CORE forever. Same for printer: domain-walk is product truth until table print has a consumer.

### Issue G4 — Severity: suggestion (dead weight)

- **Area:** `Poly/Validation/**` (~501 LOC), `Poly.Tests/Validation/**` (commented out), `Poly/Text/**` (~2993 LOC, Matching dual of Grammar)
- **Failure mode:** Every agent reads CORE/AGENTS placement and **extends the wrong module**. Compile and conceptual tax forever.
- **Evidence:** dead-dual inventory greps — zero product callers for Validation and Text.Matching.
- **Growth fix:** Delete Validation + Matching (and decide StringView/Parsers) in one cleanup suite. Update AGENTS/CORE in same PR (placement already warns).

### Issue G5 — Severity: suggestion (API surface explosion)

- **Area:** `Poly/DomainModeling/Evolution/DomainChange.cs` (~1153 LOC, 40+ change records)
- **Failure mode:** MCP unified add/remove, but **internal** evolution still grows one type per micro-edit. New features add more records, more Apply arms, more tests.
- **Evidence:** file size; MCP already maps many kinds → changes; builders/tests still think in fine-grained changes.
- **Growth fix:** Freeze new change types unless evolution **cannot** express the edit. Prefer composition of existing changes. Document “add a DomainChange” as last resort in AGENTS.

### Issue G6 — Severity: suggestion (metadata bag sprawl)

- **Area:** `Poly/DomainModeling/Analysis/*` — ~20 metadata types; pipeline registers ~20 analyzers in `DomainModelAnalyzer`
- **Failure mode:** Mid-pipeline bags + catalog + capability + storage/transport = “which lookup do I use?” Second lookup path = silent wrong member.
- **Evidence:** CORE already documents residual non-catalog bags; `DomainSemanticLookupExtensions` (~307 LOC) papers over multiplicity.
- **Growth fix:** **Catalog-first rule** enforced: new fact bags need a named consumer *before* merge. Prefer embedding into catalog over new top-level metadata. Storage/Transport clearly labeled **pack/infra**, not core ontology.

### Issue G7 — Severity: suggestion (module coupling)

- **Area:** DomainModeling → Interpretation (PolicyEvaluator, DomainEntityInstance)
- **Failure mode:** Domain runtime **is** a consumer of the platform (intentional per CORE) but deep `using Poly.Interpretation.Vm` inside instance logic makes DomainModeling hard to reason about as “just domain.”
- **Evidence:** `DomainEntityInstance` / `PolicyEvaluator` import Interpretation + Vm + Linq.
- **Growth fix:** Accept the dependency, but **narrow the façade**: e.g. `IPolicyExecutor` / `IEffectCompiler` owned at DomainModeling boundary, implemented via Interpreter. Prevents instance file from knowing emitter details.

### Issue G8 — Severity: suggestion (authoring multiplicity)

- **Area:** `.poly` / MCP add-remove / Builders / raw DomainEvolution
- **Failure mode:** Feature lands in one surface; others lag; round-trip tests only cover DSL happy paths; agents invent MCP payloads the printer cannot emit.
- **Growth fix:** **DSL + guide = product truth.** Rule: new authorable shape needs guide + printer + parser in one change. Builders are test ergonomics only unless dogfood demands them.

### Issue G9 — Severity: suggestion (effect execution dual)

- **Area:** VM-lowered effects vs direct-exec effects (`DomainEntityInstance` pipeline docs)
- **Failure mode:** New effect types pick the wrong path; semantics of “when does analysis apply?” diverge; testing matrix doubles.
- **Growth fix:** Maintain an explicit **effect execution matrix** in DomainModeling README. Prefer lower-to-Ast when the effect is pure data transform; keep direct only for graph mutation (create/link/transition/invoke).

### Issue G10 — Severity: bug (latent product contract)

- **Area:** `PolicyEvaluator` / subject property alignment
- **Failure mode:** Comment documents silent default on property name mismatch (`Ages` vs `Age` → default 0) rather than fail-closed. Growth multiplies silent wrong policies.
- **Evidence:** `PolicyEvaluator.cs` summary documents silent read of default; contradicts platform fail-closed posture for semantic paths.
- **Growth fix:** Fail closed when PropertyAccess name not on subject (or analysis-time check). Track as product correctness, not style.

### Issue G11 — Severity: suggestion (engine honesty)

- **Area:** `Poly/Grammar/Token.cs` — engine owns Text/Line/Col/Payload
- **Failure mode:** Future binary/ISA or char matching warps around text token; GrammarException forces coordinates.
- **Evidence:** grammar-revision design lock; matcher never reads `.Text`.
- **Growth fix:** Admit **grammar-revision tier A** after grammar wrap-up (or when engine work is prioritized): `IToken`, caller-supplied `GrammarException` positions, DSL zero-behavior migrate.

### Issue G12 — Severity: suggestion (process growth)

- **Area:** `docs/plans/**` (~455 md), suite README+0..N+gate+review+follow-ups pattern
- **Failure mode:** Agents treat history as CURRENT; status drifts; cognitive load exceeds code load.
- **Evidence:** complexity map D19; prior monopath fix for CURRENT; still large archive.
- **Growth fix:** Enforce PIPELINE-STATUS only; archive DONE live suites quarterly; agent search roots exclude `docs/plans/archive/`.

### Issue G13 — Severity: suggestion (Ast breadth vs product)

- **Area:** ~88 Ast node types; product domain path uses a thin subset
- **Failure mode:** Platform completeness ambition (general program IR) vs product generation funding — CORE already says generation funds neurosymbolic work. Emitter `NotSupported` gaps vs full Ast catalog.
- **Growth fix:** Explicit **product-supported Ast subset** doc for domain lower/export. New Ast nodes require a product consumer or stay experimental.

### Issue G14 — Severity: nit (product identity)

- **Area:** `Poly/Poly.csproj` package description still “application development extension…”
- **Failure mode:** Public identity mismatch with neurosymbolic domain platform.
- **Growth fix:** Update package metadata when packaging matters.

### Issue G15 — Severity: nit (naming archaeology)

- **Area:** “V3”, “Phase 1a/1b”, suite codes in comments
- **Failure mode:** Onboarding tax; agents invent V4.
- **Growth fix:** `post-v2-delete-naming-cleanup` when idle green tree.

---

## 3. Growth principles (adopt as operating rules)

Derived from the review — short enough to put next to AGENTS:

1. **One dual at a time.** Never open a third way to answer the same question (third printer, third evaluator, third constraint language).
2. **DSL is the product authoring medium.** Other surfaces must round-trip or stay non-product.
3. **No new DomainChange / metadata bag without a named consumer in the same PR.**
4. **Domain never patches the VM emitter.** Lower, analyze, replace.
5. **Delete dead duals before adding extension points.** Validation/Text first.
6. **God files get collaborators, not more methods** (instance, emitter, EffectAnalyzer).
7. **Claims match code** (pure Grammar, handlers-only IR, media-agnostic token).
8. **CURRENT is one line** (PIPELINE-STATUS). Plans are not evergreen architecture.
9. **Generation dogfood steers depth.** If export/MCP doesn’t need it, park it.
10. **Tests protect seams, not every private shape.** Prefer parity oracles over locking intermediate bags.

---

## 4. 12–24 month roadmap shape (not a suite admit)

| Horizon | Focus | Closes |
|---------|--------|--------|
| **Now** | Grammar wrap-up (LeftAssoc live-fold / span honesty) per PIPELINE | G3 |
| **Next cleanup** | Delete Validation + Text.Matching | G4 |
| **Next product** | mut-safety (session integrity for agents) | operability |
| **Engine** | grammar-revision tier A (IToken + exception positions) | G11 |
| **Runtime** | Split DomainEntityInstance collaborators | G1 |
| **When painful** | Effect execution matrix + PolicyEvaluator fail-closed names | G9, G10 |
| **Idle** | Naming cleanup, package description, archive purge | G14, G15, G12 |
| **Park** | New Ast nodes, new DomainChange types, binary Grammar consumer, full printer table-parity | G5, G13 |

**Explicitly deprioritize:** multi-host introspection completeness, Mermaid as product, Validation revival, Matching rebuild as engine justification.

---

## 5. Strengths to preserve under growth pressure

- Fail-closed defaults  
- Analysis gate on evolution  
- MCP catalog diet  
- Dual-oracle only in tests (don’t let LINQ back into product)  
- Guide-as-product-contract for DSL  
- Pack forms for extension (E1 / annotations)  

---

## 6. Follow-ups (checkable)

- [ ] **F1** — Split plan for `DomainEntityInstance` collaborators (design note or suite)  
- [ ] **F2** — Delete Validation + Text.Matching (dead-dual suite)  
- [ ] **F3** — Grammar wrap-up suite: live fold vs span honesty (PIPELINE admit)  
- [ ] **F4** — PolicyEvaluator fail-closed on missing subject property (test + fix)  
- [ ] **F5** — Effect execution matrix in DomainModeling README  
- [ ] **F6** — “No new DomainChange without consumer” line in AGENTS  
- [ ] **F7** — grammar-revision suite solidification when admitted  
- [ ] **F8** — Agent search exclude `docs/plans/archive`  
- [ ] **F9** — Product-supported Ast subset note (lower/export)  
- [ ] **F10** — Package description update when packaging  

---

## 7. Issue count

| Severity | Count |
|----------|------:|
| bug | 1 (G10 latent silent policy default) |
| suggestion | 12 |
| nit | 2 |

**Ship posture for the platform:** green tests do not mean low complexity. Long-term health = **closing duals and god-modules while dogfooding generation**, not completing every IR node.

---

## 8. Related

- [`docs/complexity-semantic-map.md`](../../complexity-semantic-map.md) — facet inventory  
- [`docs/plans/grammar-revision.md`](../../plans/grammar-revision.md) — token/exception design lock  
- [`docs/plans/dead-dual-inventory-2026-08-08.md`](../../plans/dead-dual-inventory-2026-08-08.md)  
- [`docs/agent/phenomenal-review.md`](../phenomenal-review.md) — protocol used as stance  
