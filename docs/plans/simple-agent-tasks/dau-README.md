# Domain Analysis Unification — Simple-Agent Queue (`dau-*`)

**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md)  
**Predecessor:** [`apm-README.md`](apm-README.md) (complete — registration only)  
**CORE:** [`../../CORE.md`](../../CORE.md)  
**Inventory:** [`../../domainmodeling-capability-inventory.md`](../../domainmodeling-capability-inventory.md)  
**Gate:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Rules

1. **One micro-task at a time** (one ID from the parent plan).  
2. **Phase order:** D0 → D1 → D2 → D3 → D4. Do not start D3 storage/transport until D1 wrappers are gone.  
3. **Lowering depends on Analysis** — do not reintroduce Analysis → Lowering domain-fact wrappers.  
4. **Pack surfaces:** do **not** delete Transport / coupling / capability facets because “no GetMetadata today.”  
5. **Proven residue only** for deletes (e.g. EnumConstraintSubset after inheritance removal).  
6. Mid-migration dual homes are **expected** until D1 exits — fix by finishing the move, not by freezing the bridge.  
7. Pre-ship gate before marking a phase Done.  
8. Tests grow more specific; production more generic. Prefer `DomainModelAnalyzer.Analyze` for multi-fact tests.

---

## Agent pick

```text
DONE:    APM registration; DAU plan + D0 framing (index, inventory, roadmap)
CURRENT: D1 complete — D1.5 gate passed (1611 tests).
THEN:    D2 unify walks (root+ownership, action shape, coupling)
PULL:    D3 storage+transport always-on; D4 residue/docs
```

---

## Phase 0 — Framing

| # | Task | Parent | Status | Diff |
|---|------|--------|--------|------|
| **D0.1** | Successor framing in plans index / APM status | §5 D0.1 | `[x]` | S |
| **D0.2** | Inventory: migration in flight + pack-surface policy | §5 D0.2 | `[x]` | S |

**Exit 0:** Agents pick DAU, not “post-APM random delete.” ✅

---

## Phase 1 — Collapse wrappers

| # | Task | Parent | Status | Diff |
|---|------|--------|--------|------|
| **D1.1** | Topology: algorithm + model → Analysis; one `INodeAnalyzer` | §5 D1.1 | `[x]` | M |
| **D1.2** | Aggregate/ownership: same | §5 D1.2 | `[x]` | M |
| **D1.3** | Behavior: same (may still exist until D2.2) | §5 D1.3 | `[x]` | M |
| **D1.4** | Retarget tests off Lowering dual APIs | §5 D1.4 | `[x]` | M |
| **D1.5** | Gate Phase 1 | §5 D1.5 | `[x]` | S |

**Exit 1:** No `Analysis` pass constructs `Lowering.*Analyzer` for topo/agg/beh.

**Required reading for D1:** parent §1–§2, `DomainModelAnalyzer.cs`, existing `*Pass.cs` + `Lowering/*Analyzer.cs` pair.

---

## Phase 2 — Unify overlapping cores

| # | Task | Parent | Status | Diff |
|---|------|--------|--------|------|
| **D2.1** | Root + ownership single story | §5 D2.1 | `[ ]` | M |
| **D2.2** | Capability + Behavior one action walk | §5 D2.2 | `[ ]` | M |
| **D2.3** | Topology + CrossReference coupling | §5 D2.3 | `[ ]` | M |
| **D2.4** | Effect ordering + unused param into EffectAnalyzer | §5 D2.4 | `[ ]` | S–M |
| **D2.5** | Subscription trio → one analyzer | §5 D2.5 | `[ ]` | M |
| **D2.6** | Gate Phase 2 | §5 D2.6 | `[ ]` | S |

**Exit 2:** Fewer registrations; dual walks gone for listed clusters.

---

## Phase 3 — Storage + transport in domain analysis

| # | Task | Parent | Status | Diff |
|---|------|--------|--------|------|
| **D3.1** | `Analyze(domain, DomainAuthoringContext?)` (or equiv.) | §5 D3.1 | `[ ]` | M |
| **D3.2** | Storage always-on (defaults + context maps) | §5 D3.2 | `[ ]` | M |
| **D3.3** | Transport always-on (pack-ready; do not delete) | §5 D3.3 | `[ ]` | M |
| **D3.4** | MCP/DSL session context → analyze | §5 D3.4 | `[ ]` | M |
| **D3.5** | DslCompiler emit-first | §5 D3.5 | `[ ]` | M |
| **D3.6** | Tests (domain metadata + pack variance + AllMode) | §5 D3.6 | `[ ]` | M |
| **D3.7** | Gate Phase 3 | §5 D3.7 | `[ ]` | S |

**Exit 3:** Domain analysis carries storage + transport; codegen does not re-derive domain facts.

---

## Phase 4 — Residue + docs

| # | Task | Parent | Status | Diff |
|---|------|--------|--------|------|
| **D4.1** | Proven residue only (EnumSubset / DMCS002 if dead) | §5 D4.1 | `[ ]` | S |
| **D4.2** | CORE + README + inventory sync | §5 D4.2 | `[ ]` | S |
| **D4.3** | Naming / alias hygiene (optional) | §5 D4.3 | `[ ]` | S |
| **D4.4** | Final gate → plan Complete | §5 D4.4 | `[ ]` | S |

---

## Do not pick

| Item | Why |
|------|-----|
| Delete Transport / RestApi “unused” | Pack-bound; promote into Analysis (D3), don’t scavenge |
| Storage always-on with hard-coded SQL Server types | Use context defaults + packs |
| Start D2 merge before D1 home move | Dual homes make merge thrash |
| Move PolicyEvaluator / DE lowering into Analysis | True Lowering stays |
| Reopen APM Phase A registration as if unfinished | Registration done; ownership is DAU |
| Collapse Pass/Analyzer by adding *more* wrappers | Unify into one type |

---

## Principles

- Domain fidelity + CORE seams  
- Finish the migration; mid-move weirdness is residue  
- Packs consume unified metadata  
- Tests specific; production generic  
- Fail-closed codegen retained  
- Pre-ship before phase Done  
