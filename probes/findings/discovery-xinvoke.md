# Discovery findings — discovery-xinvoke (cross-entity invoke + quantifiers + subscriptions)

Slice probes in `probes/discovery-xinvoke/`. All checked with
`scripts/run-probe.sh` (parse → export → Roslyn compile 0/0 bar) + static export
review + one runtime check via MCP (fresh session, `any`/`all` subscription
semantics — confirmed working, matching the guide).

## F1 — quantified invoke followed by more effects emits unreachable code (CS0162)
- **Signal:** compile-fail (warning)
- **Severity:** 🔴
- **Repro:** `probes/discovery-xinvoke/invoke-orders.poly` — `Ship: action { invoke all
  items.Mark(); invoke any items.MarkUrgent(level: 5); transition to Shipped }`.
  `run-probe.sh` → `errors: 0, warnings: 1`, `warning CS0162: Unreachable code detected`.
  The export emits the quantified invokes as `throw new NotSupportedException(...)` **in
  place**, so `this.CurrentStage = Shipped; return Success();` are dead code.
- **Expected:** the guide (§9) lists `invoke [any|all] Rel.Action` and the canonical
  example pairs `invoke` with `transition to` — this shape must compile 0/0.
- **Actual:** a generated CS0162 warning fails the compile bar on a legitimate,
  guide-endorsed action shape.
- **Proposed patch:** in `EffectLoweringPass.InvokeAction`, when any quantified invoke is
  present, emit a single terminal `throw` for the whole action body (aggregate all
  un-lowerable effects into one exception) instead of a per-effect throw that leaves the
  rest of the body as dead code.

## F2 — OneToOne cross-entity invoke lowers to a null-forgiving nav deref (NRE at runtime)
- **Signal:** fail-loud-but-sharp (export/runtime divergence)
- **Severity:** 🟡
- **Repro:** `probes/discovery-xinvoke/invoke-orders.poly` — `Pay` action contains
  `invoke invoice.Settle(amount: amount)`. Export: `this.Invoice!.Settle(amount);`.
  In the standalone export the `Invoice` navigation is null unless explicitly
  created/wired, so invoking `Pay` on an order without an invoice → `NullReferenceException`
  at runtime. The quantified path (F1) throws a clean `NotSupportedException`; the
  singular path instead crashes on the un-populated navigation.
- **Expected:** the runtime evaluates cross-entity invoke via the store (linked set);
  the standalone export must fail loud (throw) or guard the null — not crash with an NRE.
- **Actual:** `this.Invoice!.Settle(amount)` compiles (the `!` suppresses the warning) but
  NREs at runtime when the navigation is null.
- **Proposed patch:** lower singular cross-entity invoke to a null guard —
  `if (this.Invoice is null) return DomainResult.Failure("...");` — or throw
  `NotSupportedException` for consistency with the quantified path.
- **FIXED:** the sweep now emits a boundary guard (`if (this.Assignee == null) return
  DomainResult.Failure("'Notify' requires a linked 'assignee' on entity 'Issue'.")`)
  before the deref — no `!`, no NRE (commit follows).

## F5 — to-one path-prefix hops emit bare null-forgiving derefs (NRE in policies/conditions)
- **Signal:** fail-loud-but-sharp (export/runtime divergence)
- **Severity:** 🟡
- **Repro:** any policy with a to-one path-prefix, e.g. `IsClassic: policy { book Title is
  "Classic" }` → export `this.Book!.Title == "Classic"`. The runtime's fail-closed
  path-prefix contract (EvaluatePathPrefixChain) throws a deliberate
  `InvalidOperationException("No linked instances found for relationship 'book'.")` on an
  unlinked hop; the export's `!` yields a bare NRE (same "fail loud" intent, no message,
  no match with the runtime's contract).
- **Expected:** the export must fail loud with the same deliberate, message-carrying
  failure as the runtime — no NRE, no silent false (the runtime explicitly rejects
  vacuous true/false).
- **Actual:** `this.Book!.Title` → NRE when the nav is unlinked.
- **Proposed patch:** lower each hop to `this.Book ?? throw new
  InvalidOperationException("No linked instances found for relationship 'book'.")` — a
  throw-expression coalesce matching the runtime's message.
- **FIXED:** the hop now lowers to that coalesce (multi-hop chains guard each hop); the
  `!` is gone (commit follows).


## F3 — subscriptions on the same relationship+stage collide in the generated method name
- **Signal:** compile-fail
- **Severity:** 🔴
- **Repro:** `probes/discovery-xinvoke/subscriptions.poly` — three `when payments
  Captured` subscriptions: `when any …`, `when all …`, `when … as p`. The export emits
  `WhenPaymentCaptured()` (any), `WhenPaymentCaptured()` (all) and
  `WhenPaymentCaptured(Payment p)` (Each + binder) → `error CS0111: Type 'Order' already
  defines a member called 'WhenPaymentCaptured'` + `error CS0121: ambiguous call`.
  (Two `Each` blocks on the same relation+stage — or stage-level + entity-level — would
  collide the same way.)
- **Expected:** `any`/`all`/`Each` are three distinct, coexisting subscription semantics
  (guide §7) and must each generate a uniquely-named subscriber method.
- **Actual:** the generated method name is derived from (relation, stage) only; the
  quantifier and block position are ignored, so distinct subscriptions collide.
- **Proposed patch:** include the quantifier (and/or a sequence index) in the generated
  subscriber method name, e.g. `WhenAnyPaymentCaptured` / `WhenAllPaymentCaptured` /
  `WhenEachPaymentCaptured`.

## F4 — action/property name collision accepted by analysis, breaks the export
- **Signal:** compile-fail (silent — no analysis rejection)
- **Severity:** 🟠
- **Repro:** `probes/discovery-xinvoke/invoke-orders.poly` (original form) — an entity
  with both `Urgent: Number` (property) and `Urgent: action (level: Number)`. Analysis
  accepts both (duplicate checks are per-member-kind only); the export emits
  `public long Urgent { … }` + `public static DomainResult<Item> Urgent(long level)` →
  `error CS1656: Cannot assign to 'Urgent' because it is a 'method group'` (+ CS0102).
- **Expected:** analysis rejects a property named the same as an action on the same
  entity (fail-loud at authoring), like it rejects duplicate members within a kind.
- **Actual:** silently accepted → the export emits invalid C#.
- **Proposed patch:** extend `StructuralDomainAnalyzer.ReportDuplicates` (or add a
  cross-kind check) to reject an action whose name collides with a property/policy/
  stage/navigation on the same entity.
