# Domain Analysis Unification — Agent Queue (`dau-*`)

**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md)  
**Velocity map:** [`../platform-velocity-review.md`](../platform-velocity-review.md)  
**Gate:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

**Reviews:**  
- §13–§15: earlier false Completes.  
- **§16:** product + residual tests **accepted**; only **ops residual** = uncommitted tree.

---

## How to pick

1. First required `[ ]` below (if any).  
2. **No product DAU tasks left** unless reopened by review.  
3. **D4.4 ops:** commit when user asks — do not invent product work.

---

## Agent pick

```text
DONE:    D0–D4.2 product + tests (D4.3 optional skip)
CURRENT: D4.4 ops only — commit dirty DAU tree when user requests (product DoD met)
THEN:    Post-DAU product work
PULL:    D2.1–D2.3; D4.3 naming; D3.5 fail-message polish (“Infrastructure pipeline…”)
```

---

## Hard rules

| Rule | Why |
|------|-----|
| Lowering depends on Analysis | No dual homes |
| RestApi ≠ domain analysis | Transport emit consumer |
| Fail-closed storage without hierarchy | D3.0 |
| Emit-first happy path | No second fact world when analysis complete |
| Do not mark Done with empty DoD | §13 |
| Do not claim **ship Complete** on dirty tree without user waive | §14–§16 — product may be Done; commit still open |

---

## Phase 0–2

| ID | Status |
|----|--------|
| D0, D1, D2.4, D2.5, D3.0 | `[x]` |
| D2.1–D2.3 | `[ ]` PULL |

---

## Phase 3

| ID | Status | Notes |
|----|--------|-------|
| **D3.1–D3.5** | `[x]` | Verified in tree |
| **D3.4b** | `[x]` | Facts + MCP smoke |
| **D3.6** | `[x]` | Storage/Transport goldens + `Analyze_WithDifferentTypeMaps_ProducesDifferentColumnTypes` (varchar vs TEXT) |
| **D3.6b** | `[x]` | GenerationAssertions domain analyze |
| **D3.7** | `[x]` product | 1618 green; **tree still dirty** — pre-ship product bar met; commit is D4.4 ops |

**Phase 3 product:** ✅ met.

---

## Phase 4

| ID | Status | Notes |
|----|--------|-------|
| **D4.1** | `[x]` | EnumSubset deleted |
| **D4.2** | `[x]` | Inventory always-on |
| **D4.3** | `[ ]` optional | |
| **D4.4** | `[~]` **ops residual** | Product Complete **yes**; **ship Complete** after commit when user asks |

**Product suite Done.** Ship/ops: commit remaining dirty DAU batch.

---

## §16 audit (2026-07-25, fourth Complete claim)

### Product + tests — accept

| Check | Evidence |
|-------|----------|
| Suite | **1618** green |
| Transport | `Analyze_ProducesTransportMetadata` |
| Storage | `Analyze_ProducesStorageMappingMetadata` |
| Pack variance | `Analyze_WithDifferentTypeMaps_ProducesDifferentColumnTypes` (varchar vs TEXT) |
| MCP facts | structured smoke |
| Create + Context | yes |
| Pipeline Storage+Transport | yes |
| Storage under Analysis | yes |
| EnumSubset | gone |

### Ops residual only

| Item | Status |
|------|--------|
| Dirty uncommitted tree | Still dirty — not a product reopen |
| DslCompiler “Infrastructure pipeline” message | Optional polish |
| DomainModeling README Analysis table | Optional polish |

### Verdict

**Product DAU: Done.**  
**Ship Complete:** open until user commits (or waives). Do **not** re-open D3.1–D3.6 product tasks.

---

## Do not pick

| Item | Why |
|------|-----|
| Re-implement D3 product | Accepted §16 |
| D2.1–D2.3 | Pull |
| RestApi analysis bags | Emit only |
| Commit without user ask | AGENTS: only commit when requested |
