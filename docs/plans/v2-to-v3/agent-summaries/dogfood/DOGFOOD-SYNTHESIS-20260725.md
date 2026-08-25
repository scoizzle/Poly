# Dogfood Wave 1 Synthesis

**Date:** 2026-07-25  
**Protocol:** [`mcp-dogfood-protocol.md`](../../mcp-dogfood-protocol.md) §6  
**Scenarios:** S1 (library checkout), S2 (reassign/link), S3 (owned profile)  
**Prior rounds:** July 18 reports (R gap shipped as Runtime MCP)  
**Fix pass shipped this wave:** G1 (simulate_policy fail-closed), G3 (StoragePass rollback noise), HOST (runtime tools enabled)

---

## 1. Open blockers (ranked by score × scenario impact)

| Rank | ID | Scenario | Bucket | Score | Title |
|------|----|----------|--------|-------|-------|
| 1 | S2-B1 | S2 reassign | **C** | 13 | No unlink/reassign — link\_instances adds but never removes; no unlink tool or DSL keyword |
| 2 | S1-R-B1 | S1 checkout | **R** | 12 | `require not PolicyName` negation — guard fails when policy is false; negation lost in evaluation |
| 3 | S3-B1 | S3 owned | **I** | 12 | OwnedAccess expression path IR-only — `DomainExpression.OwnedAccess` exists but DSL and JSON policies reject `profile.City` |
| 4 | S2-B2 | S2 reassign | **M** | 10 | `create in` children not registered in session InstanceMap — child exists in domain but `list_instances` returns 0 |
| 5 | S1-B1 | S1 checkout | **M** | 10 | invoke\_action disabled (FIXED via HOST — not product; removing from open) |
| 6 | G4 | Mutation | **S** | 8 | Tool enablement inconsistent across tool group activations |
| 7 | S2-B3 | S2 reassign | **W** | 7 | `get_instance` does not expose navigation property values — only scalars returned |
| 8 | S3-B2 | S3 owned | **W** | 7 | No atomic create-entity-with-owned-sub-entity — must create + link separately |
| 9 | G2 | Mutation | **W** | 6 | `get_policy_expression` returns raw AST dump instead of structured JSON (optional) |

**Removed from open backlog** (shipped in fix pass):  
- G1 (simulate\_policy fail-closed) — `CollectPropertyNames` guard added  
- G3 (StoragePass rollback noise) — `HasStructuralFailure` guard added  
- HOST (invoke\_action disabled) — runtime tools enabled  

---

## 2. Cross-cutting concept clusters

### Cluster A: Link graph (rank 1, 4, 7 — score 13/10/7)
The highest-scoring cluster. S2 found that `link_instances` adds but cannot remove, `create in` children are invisible to `list_instances`, and `get_instance` doesn't show navs. Together they mean **the runtime graph model is append-only and mostly invisible**. Reassigning existing instances, verifying parentage, and discovering children are all broken in product MCP.

**Symptoms across scenarios:** S2 blocked entirely; S3 workaround for owned linking; S1 create-in registration gap.

### Cluster B: Expression/guard honesty (rank 2 — score 12)
`require not PolicyName` silently does nothing — it should negate the policy result but doesn't. This is a runtime evaluation bug in the action pipeline, not a missing feature. It blocks the entire lifecycle execution path for any action using negation.

### Cluster C: Owned/nested access (rank 3, 8 — score 12/7)
`owned` declaration is shipped and works. But you cannot **read** owned fields in a policy or effect expression (DSL rejects dot syntax; JSON add\_policy doesn't support OwnedAccess). And you cannot **create** an entity with its owned sub-entity atomically. Together this makes the `owned` concept declarative-only — no runtime enforcement possible.

### Cluster D: MCP runtime fidelity (rank 4, 6 — score 10/8)
`create in` children not appearing in `list_instances`, and intermittent tool enablement issues. These reduce agent trust in the runtime tools — they can't tell if an operation failed or the tool is lying.

---

## 3. Next build slice (ONE)

### Recommendation: **Link/unlink runtime & MCP (cluster A)**

**Why this slice:**
- Highest total open score (13+10+7 = 30 across 3 blockers)
- Blocks S2 entirely (reassign is impossible without it)
- The `link_instances` tool exists and works for creating links — the missing half (unlink, visibility) is the natural completion of that seam
- Prerequisite for any graph-based domain (ownership changes, child migration, reassignment)
- S3 owned-entity linking also benefits

**Thinnest vertical:**

| Step | What | Size |
|------|------|------|
| 1 | Add `unlink_instances` MCP tool — removes a single store link between two instances for a given relationship | S |
| 2 | Register `create in` children in session InstanceMap so they appear in `list_instances` | S |
| 3 | Expose navigation property values (target instance IDs) in `get_instance` response | S |
| 4 | Add DSL `link` / `unlink` effect keywords so actions can express "assign entity X to relationship Y" | M |
| 5 | Runtime evaluation for link/unlink effects (DomainExpression lowering, VM execution) | M |

**Total:** M (S+S+S+M+M — 3 small, 2 medium)

**Alternative considered:** "Expression guard honesty" (cluster B, require-not fix). Also score 12 but narrower — it's a runtime bug fix, not a missing feature. It should be done **in parallel** or immediately after step 1 since it's independent and smaller.

**Alternative rejected:** "OwnedAccess expression path" (cluster C). Score 12 but requires DSL parser changes + JSON deserializer changes + tests. Larger blast radius. Defer until link/unlink slice is shipped.

### Micro-tasks for the slice

```
dogfood-link-1  — Add unlink_instances MCP tool
dogfood-link-2  — Register create-in children in InstanceMap
dogfood-link-3  — Expose nav property IDs in get_instance
dogfood-link-4  — Add DSL link/unlink effect syntax + parser
dogfood-link-5  — Runtime evaluation for link/unlink effects
```

---

## 4. Explicit non-actions

| Item | Why |
|------|------|
| OwnedAccess expression path (S3-B1) | Deferred after link/unlink slice — larger scope, DSL+JSON changes |
| `get_policy_expression` AST formatting (G2) | Optional — workaround exists (`describe_expression`) |
| Codegen / DAU / packs | Out of scope for dogfood entirely |
| `require not` negation fix (S1-R-B1) | Do separately — it's a runtime bug fix, independent of link/unlink |
| Second synthesis pass | Only after the above slice ships |

---

## 5. All findings table

### Shipped this wave

| ID | Fix | Size |
|----|-----|------|
| G1 | `simulate_policy` fail-closed on missing properties (CollectPropertyNames guard) | S |
| G3 | StoragePass skips on HasStructuralFailure — guards no longer drown out real errors | S |
| HOST | Runtime MCP tools enabled — invoke\_action, link\_instances, create\_instance callable | S |

### Open backlog

| Priority | ID | Bucket | Score | One-line fix | Slice |
|----------|----|--------|-------|--------------|-------|
| 1 | S2-B1 | C | 13 | Add unlink\_instances + DSL link/unlink effects | Link/unlink |
| 2 | S1-R-B1 | R | 12 | Fix require-not negation in action guard evaluation | Guards |
| 3 | S3-B1 | I | 12 | Wire OwnedAccess into DSL policy parser + JSON deserializer | Owned |
| 4 | S2-B2 | M | 10 | Register create-in children in session InstanceMap | Link/unlink |
| 5 | G4 | S | 8 | Stabilize tool group activation (host-side) | Infrastructure |
| 6 | S2-B3 | W | 7 | Expose nav property IDs in get\_instance response | Link/unlink |
| 7 | S3-B2 | W | 7 | Support inline owned entity data in create\_instance | Owned |
| 8 | G2 | W | 6 | Serialize get\_policy\_expression as JSON (optional) | Optional |

---

## 6. Historical comparison with prior rounds

| Round | Top finding | Product response | Status |
|-------|-------------|------------------|--------|
| July 18 (C9) | No CallAction in MCP (R, score 18) | Shipped `create_instance`, `invoke_action`, `list_instances`, `link_instances` | ✅ Shipped |
| July 18 (C4) | API honesty — AddActionToStage creates empty copies | Guide/docs fixes | ✅ Honest |
| Wave 1 (S2-B1) | No unlink/reassign (C, score 13) | **Next build slice** | ⬜ |
| Wave 1 (S1-R) | require-not negation (R, score 12) | **Do in parallel** | ⬜ |

The platform has moved from "runtime doesn't exist" (July 18) to "runtime exists but graph is append-only" (Wave 1). The next logical step is completing the graph model.
