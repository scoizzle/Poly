# Discovery findings — discovery-a (CROSS-ENTITY INVOKE + COLLECTION QUANTIFIERS)

Slice: any/all/none/count quantifier policies, `invoke [any|all] Rel.Action [where …]`,
path-prefix reads, `Rel exists`. Pipeline: probes → `scripts/run-probe.sh` (parse/analyze/
export + Roslyn compile-check) → static review of the generated C# → throwaway TUnit runtime
probes (deleted from the tree after use).

Probes:
- `probes/discovery-a/issue-tracker.poly` — Tribes/Engineers/Issues; OneToOne + OneToMany
  cross-entity invoke (self, with args, quantified with `where`), Q3′ policies, singular-nav
  `exists`, 2-hop path-prefix.
- `probes/discovery-a/library.poly` — Patron/Loan/Hold/Fine/Book; quantified invoke with
  args (`invoke any loans.LoanFine(amount: 5) where Status is "Overdue"`), Q3′ empty-set
  policies (`count … == 0`), single-hop path-prefix.
- `probes/discovery-a/ecommerce.poly` — Customer/Order/Line/Invoice/ShippingAddress;
  quantified invoke, `Rel exists` on `many` and to-one, entity-level policy gating.

Runtime checks (throwaway `Poly.Tests/Mcp/ZzDiscoveryAProbeTests.cs`, deleted):
- `orders exists` with zero outbound links → **false** at runtime.
- `invoke all Rel.Action` with zero linked targets → fails loud ("matched zero targets").
- `invoke any Rel.Action(amount: 7) where Total > 10` → only the matching target charged.
- `invoke any Rel.Action() where Status is "Open"` with enum-typed `Status` → filter matches.
- `invoke invoice.Submit` with no link → fails loud ("No linked instances").
All runtime semantics matched the guide; the divergences below are export-side.

---

## F1 — Multi-hop path-prefix: nested relationship hops are not PascalCased in the C# export (CS1061)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** cross-entity invoke + collection quantifiers (path-prefix reads)
- **Repro:** `probes/discovery-a/issue-tracker.poly` — `Issue` policy
  `FromBlueTeam: policy { reporter team TeamName is "Blue" }`; run `scripts/run-probe.sh probes/discovery-a/issue-tracker.poly`.
- **Expected:** exported `public bool FromBlueTeam() => this.Reporter.Team.TeamName == "Blue";`
  (nav `team` → generated member `Team`). The guide documents multi-hop to-one path-prefix as
  shipped (`loan book Title is "Classic"`).
- **Actual:** `scripts/run-probe.sh` reports `error: (282,49): error CS1061: 'Engineer' does not contain a definition for 'team'`.
  Export emits `this.Reporter.team.TeamName` — the **first** hop (`reporter`) is PascalCased
  correctly, the **nested** hop (`team`, a nav on the *target* Engineer) keeps its raw DSL name.
- **Root cause:** `EffectLoweringPass.BuildNavigationNameResolver` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:70`)
  resolves nav names against the **current** entity's relationship set only. `DomainExpressionLoweringPass.RelationshipNavigation`
  (`DomainExpressionLoweringPass.cs:107`) applies the resolver to every hop, but the resolver
  has no mapping for target-entity navs → identity → raw name. Any multi-hop path-prefix whose
  intermediate nav has a lowercase DSL name fails to compile; guide's own `loan book …` example
  is affected (nested `book`). The runtime (`EvaluatePathPrefixChain`) handles this case correctly.
- **Proposed patch (not applied):** make `BuildNavigationNameResolver` hop-aware — map each
  `RelationshipNavigation` name through the metadata of the *source entity at that hop*
  (resolve via `RelationshipLookupMetadata` for the hop's subject entity type, falling back to
  PascalCase for any relationship-resolved name). Alternatively, in `RelationshipNavigation`
  when `_navigationNameResolver` misses, PascalCase unconditionally (navs are always
  PascalCased in the exporter).

---

## F2 — `Rel exists` on a `many` relationship: export silently returns `true` always; runtime returns `false` on empty links
- **Signal:** export/runtime divergence (silent — no throw)
- **Severity:** 🟠
- **Slice:** cross-entity invoke + collection quantifiers (`Rel exists`)
- **Repro:** `probes/discovery-a/ecommerce.poly` — `Order.HasLines: policy { lines exists }`.
  Export: `public bool HasLines() => this.Lines != null;` (collection ctor-initialized →
  never null → always true). Runtime probe (deleted test): `orders exists` with zero outbound
  links evaluates **false**.
- **Expected:** per guide, empty outbound links → `false`; where the export cannot match the
  store-aware runtime it must **fail loud (throw)**, not run a different behavior.
- **Actual:** export answers `true` for a fresh Order with no linked Lines — a silent wrong
  answer. Since entity-level policies gate every action, `HasLines` (always true) is also a
  silent no-op guard in the export, while the runtime correctly blocks empty-set cases.
- **Proposed patch (not applied):** in `DomainExpressionLoweringPass.Exists/NotExists`
  (`DomainExpressionLoweringPass.cs:122-126`), detect when the `exists` target is an outbound
  `many` relationship and fail loud (throw `NotSupportedException`, same pattern as the
  Q3′ quantifiers) instead of emitting `collection != null`. To-one navs may keep the null
  comparison (matches runtime empty→false).

---

## F3 — Quantified invoke export emits `throw …; return Success();` → CS0162 unreachable-code warnings; run-probe.sh 0-warning gate fails on shipped surface
- **Signal:** compile-fail (warnings) / fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** cross-entity invoke (quantified `invoke any/all Rel.Action`)
- **Repro:** `probes/discovery-a/library.poly` (`NotifyOverdue`, `MarkAllReturned`,
  `RemindPending`, `BulkFine`) → `warnings: 5` (4× CS0162 + 1× CS8602);
  `probes/discovery-a/ecommerce.poly` (`CancelAll`, `ExpediteRush`, `ChargeAll`) → `warnings: 3` (3× CS0162).
- **Expected:** `scripts/run-probe.sh` demands 0 errors / 0 warnings; quantified invoke is
  shipped surface, so a valid domain must not trip the gate.
- **Actual:** `EffectLoweringPass.InvokeAction` (line 237) emits a `ThrowStatement` for
  quantified invoke and the action body still appends `return DomainResult.Success();` →
  CS0162 "Unreachable code detected" per quantified-invoke action. The throw itself is the
  documented fail-loud export contract (matches existing exporter test), but the trailing
  return makes every such domain fail the warning gate.
- **Proposed patch (not applied):** when the lowered body's final statement is a `ThrowStatement`,
  omit the trailing `return DomainResult.Success();` (or return it inside an `else`).
  Applies to the policy-lowering path too (F4).

---

## F4 — Single-hop to-one path-prefix export dereferences a possibly-null nav → CS8602 warning
- **Signal:** compile-fail (warnings)
- **Severity:** 🟡
- **Slice:** path-prefix reads
- **Repro:** `probes/discovery-a/library.poly` — `Loan.IsClassic: policy { book Title is "Classic" }`
  → `public bool IsClassic() => this.Book.Title == "Classic";` with `Book? Book` → CS8602
  (one of the 5 warnings above).
- **Expected:** 0 warnings. Runtime on empty link throws (fail loud); export NREs at runtime,
  so behavior is fail-loud-consistent, but the generated code trips the 0-warning gate and is
  null-loud rather than intentional.
- **Actual:** generated `this.Book.Title` without null-forgiving on a nullable nav.
- **Proposed patch (not applied):** emit `this.Book!.Title` for path-prefix leaves on nullable
  to-one navs (the exporter already uses `!` for cross-entity invoke targets, see
  `EffectLoweringPass.InvokeAction` line 257).

---

## F5 — Any entity with a Q3′ quantifier policy has every exported action dead-ended (all entity-level policy gates throw)
- **Signal:** fail-loud-but-sharp / modeling trap
- **Severity:** 🟡
- **Slice:** cross-entity invoke + collection quantifiers (quantifier policies as guards)
- **Repro:** `probes/discovery-a/ecommerce.poly` — `Customer` declares `IsVip`, `AllPaid`,
  `NoRefunds`, `TotalOrders`, `ZeroOpen`. Export: `CancelAll()` first calls
  `this.IsVip()` … `this.ZeroOpen()` — each a `throw new NotSupportedException(...)`. So even
  `CancelAll`, whose *own* body is the quantified invoke, throws at the first entity-level
  policy guard, before reaching anything action-specific.
- **Expected:** entity-level policies gate every action (documented runtime contract), but the
  export should fail loud at the *relevant* surface, not make the whole action surface of the
  entity unusable regardless of action.
- **Actual:** every action method on the entity throws `NotSupportedException` on the first
  store-dependent policy call, so the export cannot express *any* behavior of that entity —
  the shipped Q3′ surface dead-ends the whole entity in the export. (Consistent with the
  documented "entity-level policies gating every action" trap, but sharpened: the gate itself
  is a guaranteed throw.)
- **Proposed patch (not applied):** document the limitation (export contract note) and/or
  emit quantifier-policy gates as `true` with a warning when the action is otherwise
  uncallable — preferable to silently different behavior is a hard error at the action entry
  with the *policy* name, which the current code effectively does, so a doc/ADR note plus the
  F3 cleanup is likely sufficient.

---

## Non-findings (verified, no bug)

- Empty-set quantifier semantics at runtime match the guide exactly: `any`→false,
  `all`→false (no vacuous), `none`→true, `count`→0 on empty links
  (existing `Quantifiers_EmptyLinks_Honesty_ViaMcp` + `EvaluateCountExpr`/`EvaluateAllExpr`
  in `DomainEntityInstance.cs`).
- Quantified invoke zero-match fails loud at runtime ("matched zero targets") — matches the
  guide's "zero matches fail" rule.
- `invoke any Rel.Action(param: x) where …` filters correctly per-target (runtime).
- `where` filter with enum-typed target properties works at runtime (no enum-context gap).
- OneToOne cross-entity invoke with no link / multiple links fails loud at runtime.
