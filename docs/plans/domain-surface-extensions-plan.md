# Domain surface extensions — export peer, entity-level when, owned policies

**Date:** 2026-08-02  
**Status:** Done (gate G1–G6 [x] 2026-08-02)  
**Suite:** [`simple-agent-tasks/spe-README.md`](simple-agent-tasks/spe-README.md)  
**Context:** Subscription peer binding (`when Rel Stage as name`) shipped on the **VM stage-scoped** path. Dates deferred (pack debate). This plan covers the three non-date next slices.

---

## 1. Goals

| Workstream | Outcome |
|------------|---------|
| **E — Export peer** | C# export generates peer-aware subscription handlers; no throw on `as name` |
| **L — Entity-level when** | Store notify honors `Entity.Subscriptions` (any stage); guide matches runtime |
| **O — Owned policy reads** | Owned / to-one nested reads honest in guide + evaluable end-to-end under store |

Non-goals: date pack vs core; DSL link/unlink; actors; grammar rewrite; DAU/Bar B.

---

## 2. Shipped state (post SPE gate, 2026-08-02)

### E — C# export peer ✅

- Peer-dependent handlers: `When{Target}{Stage}(TargetType peerBindingName)`.
- Notify: `sub.When…(this)` with transitioned instance.
- Binder path-prefix lowers to the peer **parameter** (not `this`).
- Goldens: `Export_PeerDependentSubscription_*` in `DomainToCSharpExporterTests`.

### L — Entity-level when ✅

- Entity-level dispatch plan published; `NotifyTransition` runs **stage first, then entity-level**.
- Entity-level fires regardless of subscriber stage; peer binder allowed + validated.
- Goldens: `EntityLevelSubscription_*` in `DomainEntityInstanceTests`.

### O — Owned / to-one policy reads ✅ (thin vertical)

- Path-prefix owned/to-one evaluates under store+link; fail-closed without store/link.
- MCP golden: `EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse`.
- Residual (documented in guide): bag-null `Rel exists` sibling path; multi-hop owned; OwnedAccess IR-only.

---

## 3. Parallelism model

```text
                    ┌── SPE-E0 → E1 → E2 → E3 ──┐
  SPE-0 design  ────┼── SPE-L0 → L1 → L2 → L3 ──┼── SPE-G gate
                    └── SPE-O0 → O1 → O2 → O3 ──┘
```

- **SPE-0** (optional short lock): naming contracts only — read in parallel workstreams; no code.
- **E / L / O chains are independent** after SPE-0 (or with SPE-0 as soft reading).
- Within a chain, tasks are sequential.
- **Do not** share files across chains without finishing one edit first:
  - **E owns:** `DomainToCSharpExporter`, export tests, guide § export peer
  - **L owns:** `DomainInstanceStore`, `RuntimeContractAnalyzer` (entity plans), entity-level analysis messages, stage/entity tests
  - **O owns:** policy eval / expression preprocess for owned, owned tests, guide § owned
  - **Conflict risk:** `poly-dsl-guide.md` — each workstream edits **only its section** (see task files).

---

## 4. Design locks (read before implementing)

### Peer export (E)

1. Handler parameter type = relationship **target** entity CLR type name.
2. Parameter name = `PeerBinding` (required when exporting peer-dependent subs).
3. Notify calls `handler(this)` from the transitioned instance.
4. Lowering: binder path-prefix roots resolve to the **parameter**, not `this` (mirror action-parameter / UseThisReference patterns).
5. Notification-only (no binder) keeps zero-arg handlers.

### Entity-level when (L)

1. Product intent matches `Entity` remarks: always-active subscriptions.
2. Prefer publishing entity-level dispatch metadata once (domain or entity node) and consulting it in `NotifyTransition` **in addition to** stage plans — do not require faking a stage.
3. After dispatch works: allow `as name` on entity-level (drop the hard error that only existed because dispatch was missing).
4. Stage-scoped + entity-level both fire when both match (document order: stage first, then entity-level — or entity-level then stage; pick one and test).

### Owned policies (O)

1. Prefer **one** authoring surface: path-prefix (`profile City`) = product; OwnedAccess IR remains for lowering if needed.
2. Fail closed without store/link when policy needs owned/related data (no vacuous true).
3. Guide must not claim runtime that only parse/print works.

---

## 5. Done definition (suite)

1. Export: peer-dependent `when … as name` generates compile-shaped Syntax IR + golden test; refuse path deleted or narrowed to unsupported edge cases only.  
2. Entity-level: store notify fires entity-level subs; analysis messages match; peer binder allowed when stage-equivalent rules pass.  
3. Owned: at least one end-to-end policy golden (create + link owned + evaluate); guide honest; open gaps listed or fixed.  
4. Suite green; guide updated in the same change as surface behavior.  
5. Follow-ups land in docs if residual.
