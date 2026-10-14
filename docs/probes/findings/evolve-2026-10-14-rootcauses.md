# F26–F35 — Root causes and likely changes (research, not fixes)

Prepared for the fix pass that follows the F1–25 work. Product code was **read, not modified**;
every claim cites the current master (`bd416609`). Repro probes: `docs/probes/evolve-2026-10-14/`
(mission report: `docs/probes/findings/evolve-2026-10-14.md`, PR #175).

---

## Cross-cutting notes before per-finding detail

- **Two execution worlds share one lowered tree.** Action *effects* lower via `EffectLoweringPass`
  (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs`); *policy expressions* lower via
  `DomainToCSharpExporter.LowerExpressionToMethodBody`
  (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:418-448`) and are cached by
  `RuntimeAnalysisCache.CompletePolicyBodies`
  (`Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:386-412`). The C# print and the VM
  simulate consume these same trees. Whenever VM and print disagree, the bug is in the **VM
  execution** of the tree or in a **second tree built by a different path** — the fixes for
  F26/F27/F31 all live at that boundary.
- **Guard evaluation = policy evaluation.** `DomainEntityInstance.EvaluatePolicy`
  (`Poly/DomainModeling/Runtime/DomainEntityInstance.cs:429-449`) runs the cached policy body
  on the VM; `InvokeAction` uses it for `require` gates (lines 624-658). So a policy-eval bug
  is also a guard bug (F26 makes rate limiters no-ops).
- **Conflicts with the F1–25 agent:** F1 (Today local/UTC split) and F23 (Date<Now analysis
  miss) touch `TemporalLowering`/`TemporalMeaning` — coordinate before editing those files;
  F26's fix may land in the same expression-lowering area. F27's fix sits exactly where the
  C8 typed-create work (#162/#163) landed — re-test the C8 suites when changing it.

---

## F26 — policy-eval path mis-evaluates `Now ± N <unit>`; rate limiters never limit on the VM

**Observed:** `count requests where SeenAt > Now - 60 Minutes` always false on the VM;
`Now - 60 Minutes` in an `assign` computes correctly; exported C# is correct
(`DateTime.UtcNow.AddMinutes(-60L)`); bare `Now` in policies works; `+ 60 Minutes` works;
`- 60 Minutes` behaves like a positive offset. All five observations are consistent with the
negative offset being lost or flipped during **VM execution of a policy tree**.

**Evidence / loci:**
- Policy bodies for the VM come from `CompletePolicyBodies` → `LowerExpressionToMethodBody`
  (`RuntimeAnalysisCache.cs:386-412`) — the *same* lowering the print uses, so the tree itself
  prints correctly; the mis-evaluation is in the VM's compile/run of that tree.
- Assign effects take the other path (`EffectLoweringPass` → module body → VM) and work,
  which isolates the failure to how the VM compiles the policy body's
  `Invoke(Member(DateTime.UtcNow, "AddMinutes"), <negative literal>)`
  (see `DirectVmAbiEmitter.Expressions.cs` static-member handling and
  `DirectVmAbiEmitter.Invoke.cs` argument marshalling; `VmValueMarshaller.MarshalToClr`
  line 36-49 is plain `Convert`, so the suspect is earlier: negative constant emission or
  the ABI's invocation-argument typing, the same area that produced F31's
  "cannot tell whether argument … is a scalar or a reference" at
  `DirectVmAbiEmitter.Invoke.cs:786-789`).

**Likely change (recommended order):**
1. **Test first:** a TUnit case that `evaluate_policy`s `SeenAt > Now - 60 Minutes` against a
   store-linked request created via an action (mirrors the probe), asserting true — and the
   same for a `require` gate blocking an action. Run under `VmTrace` to capture the compiled
   tree for the failing expression.
2. **Fix at the VM:** make negative `long`/`double` literals survive ABI argument emission for
   static-member invocations inside policy trees (and add the scalar/reference resolution for
   DateTime-typed invoke results — shared with F31).
3. **Alternative/complement:** run policy bodies through the same compile pipeline as effect
   bodies (unify `CompileBody` providers) instead of the exporter-lowered cached body, so one
   VM path handles both.
- Tests: `Poly.Tests` under the interpretation/policy area; keep the ratelimit probe as an
  end-to-end regression.

---

## F27 — subscription handlers fire twice for `create in`-created children (regression)

**Observed:** one transition of a create-in child fires every handler twice (2 fines, badge +2,
2 notes); children linked via the `link_instances` tool fire once; master `a6203103` fired once
for the same shape.

**Root cause (pinned):** two registration routes for create-in children under simulate.
- Route 1 — store link: `DomainEntityInstance.HostAbi.LinkRelated` → `DomainInstanceStore.Link`
  (`Poly/DomainModeling/Runtime/DomainInstanceStore.cs:363-382`) → `AddSubscriber`
  (`DomainEntityInstance.HostAbi.cs:274-286`, deduped by `ReferenceEquals`).
- Route 2 — kept typed body: after #162 ("C8-tc1a: typed by-type create lowering"), static
  `Type.Create` is rebound to `DomainInstanceHostJobs.TypedCreate` (`DomainInstanceHostJobs.cs:45-48`),
  which delegates to the **ambient** host (`DomainEntityInstance.Ambient`,
  `DomainEntityInstance.cs:951-961`), performing the full create-in link; the kept
  `Create{Nav}`/`Attach{Nav}` module bodies then **also** run their printed registration
  statements. Verified in the current print for the probe: `AttachNotifications` emits
  `this._notifications.Add(child); child.RegisterUserNotificationsReadSubscriber(this);
  child.RegisterUserNotificationsUnreadSubscriber(this);` — and
  `DomainEntityInstance.cs:920-925` keeps exactly the Create*/Attach* bodies for simulate.
  The compiled `Register…Subscriber` body does a raw list add against the same registry
  bag key `AddSubscriber` manages, but **without** the ReferenceEquals dedupe that guards
  the store path — so the registry ends up with the subscriber twice. Tool `link_instances`
  only ever performs route 1 — hence the single/double matrix.

**Likely change (options, smallest first):**
- Make the compiled `Register…Subscriber` body (or the kept Attach/Create body under
  simulate) use the same deduped host job as `DomainEntityInstance.AddSubscriber`
  (`DomainEntityInstance.HostAbi.cs:274-286`), or skip the registration statements in kept
  bodies when the ambient `TypedCreate` already linked — `DomainEntityInstance.cs:920-945`
  (where kept bodies are assembled) is the natural spot.
- Alternatively, add the ReferenceEquals dedupe to the register method body itself so any
  caller is idempotent.
- Tests: extend the C8-tc1a suite with a fire-count regression (create-in child, one
  transition, handler increments a counter → assert +1); re-run the probe.

---

## F28 — creating a child INTO a watched stage does not fire subscriptions

**Root cause:** the create-in path (host job → ambient `TypedCreate` → store link, see F27)
never runs the `Notify{Stage}Subscribers` dispatch; only `StageTransitionEffect` lowering
emits the notify call (`EffectLoweringPass.cs:640-644`) and only transitions reach
`Store.NotifyTransition`. The printed C# behaves the same (parity today, wrong for the
badge pattern).

**Likely change:** emit the notify in the **shared create-in lowering** when analysis says the
created child's initial stage is watched (so print and simulate change together and stay in
parity): in the create-in effect lowering, after the link, emit
`child.Notify{InitialStage}Subscribers()` guarded by the subscription plan. Alternatively,
document transition-only semantics in guide §7 and add an advisory hint — but the badge
pattern (real-app bread and butter) argues for firing.
- Coordination: any/all/Each quantifier semantics on the created set need the same reasoning
  as transitions (F14's set-state rules); `previousStage` for a creation is "none".

---

## F29 — entity-level actions bypass stage gating; no stage-membership guard exists

**Root cause:** no stage-membership predicate exists in the expression language (stage names
resolve as properties — see mission-1 evidence), and entity-level actions have no stage
context. Note the machinery exists: `Stage.Policies`
(`Poly/DomainModeling/Ontology/Stage.cs:14`) and stage-guard evaluation
(`DomainEntityInstance.cs:644-655`); the DSL simply cannot author stage-scoped policies yet
(the guide says they are "a model-level surface; the DSL does not yet author them").

**Likely change (options):**
- **Smallest:** analysis advisory (hint) when an entity-level action exists on an entity that
  declares stages ("action X is available in every stage").
- **Real fix:** authorable stage-scoped policies (`Active: stage { policy … }` or a
  `require in StageName` gate on entity-level actions). This is a parser + grammar + analysis
  + lowering feature; the guard-eval machinery and export require-gate emission already
  support stage policies, so the work is mostly grammar/printer + a lowering wire-up.
- Tests: authoring round-trip, guard behavior per stage, export gate emission.

---

## F30 — no aggregation; `Total == sum lines Price * Qty` is inexpressible

**Root cause:** the expression grammar ships only `any/all/none/count`
(quantifier section of guide §8); there is no aggregate node. `sum lines …` misparses as a
relationship reference ("Relationship name 'sum' is not defined").

**Likely change:** add a `SumExpr` alongside `CountExpr`:
- Parser/grammar: `sum Rel [where body] PropExpr` (or fold into the quantifier rule).
- AST node (`Poly/Ast/`), analysis validation (OneToMany source-only, body/target property
  typing — reuse the CountExpr analysis).
- Lowering: `EffectLoweringPass`/`LowerExpressionToMethodBody` → foreach accumulate with a
  `long`/typed accumulator (print gets `foreach … sum += x.Price * x.Qty` or LINQ `Sum`);
  VM path is the same shared tree, so one lowering covers both.
- Printer round-trip + guide §8 update.
- This is the largest of the ten; size it as its own task.

---

## F31 — DateTime-typed `Now ± N Minutes` in a create initializer rejected by the VM

**Root cause:** `DirectVmAbiEmitter.Invoke.cs:786-789` —
`ctx.Analysis?.GetResolvedType(arg)` fails to resolve the type of the
`DateTime.UtcNow.AddMinutes(30)` argument in create-initializer position, throwing
"cannot tell whether argument … is a scalar or a reference." DateOnly initializers work
(`DateOnly.FromDateTime(...)` resolves) and DateTime works in assign position, so the gap is
the VM's type resolution for a DateTime-valued invoke expression at the create-argument call
site (the argument flows as `object` through the pair-bag host jobs;
`DomainEntityInstance.Runtime.cs:169-180`).

**Likely change:** in `DirectVmAbiEmitter.Invoke.cs`, resolve invoke-expression results and
DateTime-family CLR types in the create-argument context (fall back to
`GetRuntimeType() is { IsValueType: true }` from the expression's own lowered type when
`GetResolvedType` is null — mirroring `ParameterIsValueType`, lines 792-798). Add the F31
probe as a regression test.

---

## F32 — `create_instance` stores JSON values un-coerced (ISO dates stay Strings)

**Root cause:** `Poly.Mcp/Tools/RuntimeTool.cs:153-232` —
`CreateInstance` deserializes with `JsonElementToValue(je)` (raw string/number/bool) and
passes the dictionary straight to `DomainEntityInstance.Create(entity, propertyValues, domain)`
with **no per-property coercion against the entity's declared property types**. The coercion
machinery already exists for action args (`CoerceActionArgForSetArgs`,
`DomainEntityInstance.cs:474-482` → `DictionaryBackedValue.CoerceReadValue`); the create path
just doesn't use it.

**Likely change:** in `RuntimeTool.CreateInstance` (or better, inside the DEI create so both
tool and future callers benefit), walk `entity.Properties`, look up each provided key's
declared type, and coerce (`DateTime`/`Date` ← ISO strings, `Number` ← integral/range-checked,
`Boolean` ← 0/1/bool, enums ← member names) before constructing — reusing
`CoerceReadValue`/`DomainTypeMapping`. Reject values that cannot be coerced (fail closed).
- Related: F15 (0.25 truncation) is the Number face of the same boundary; fix both together.
- Tests: MCP-level create_instance with ISO date strings and fractional numbers.

---

## F33 — ManyToMany authorable via `add()` but `export_dsl` degrades it to `many`

**Root cause:** `Poly/DomainModeling/Language/DomainDslPrinter.cs:259` —
`isMany = Cardinality is OneToMany or ManyToMany` → prints `many `. The DSL has no M:N
syntax, so round-trip silently changes cardinality. The incremental tool accepts M:N
(`Poly.Mcp/Tools/DomainTools.cs:729-746`); analysis and the VM store handle M:N links fine.

**Likely change (smallest):** make `export_dsl` fail closed for domains containing
ManyToMany relationships ("ManyToMany relationship 'X' cannot be expressed in the shipped
DSL") instead of degrading; the incremental path stays session-only until the DSL grows M:N.
**If** M:N authoring is wanted: parser + grammar + printer + analysis work — defer, it's a
feature not a fix.
- Tests: add M:N via `add`, assert export_dsl fails with the named error; re-apply of a
  degraded text must no longer be possible.

---

## F34 — owned-target unlink minimum (C6c) shipped but absent from the guide

**Root cause:** guide drift. Code: `DomainInstanceStore.Unlink`
(`Poly/DomainModeling/Runtime/DomainInstanceStore.cs:390-402`, throws
`Relationship.UnlinkViolationMessage` when `SourceOwnsTarget` and owner count == 1).
Guide (`Poly.Mcp/Docs/poly-dsl-guide.md`) has no mention of the rule in §4 (`owned` table),
§7/§9 (linking), or §12 (`unlink_instances`).

**Likely change:** one-paragraph guide update in the same change (AGENTS.md requires guide
sync for behavior changes): owned declaration table (§4), unlink description (§9/§12), and
`get_dsl_guide` short-guide mention. No code change.

---

## F35 — `Duration`/`Time` properties are dead surface

**Root cause:**
- `assign Window to 90 Minutes` rejected by `TemporalMeaning.cs:98` ("bare duration with no
  temporal left operand") — by design per guide, but nothing else produces a Duration.
- `assign Elapsed to Finished - Started` (two DateTimes) rejected by the generic arithmetic
  type-check ("arithmetic operand is not numeric") — Date−Date is never folded to a
  `DateOperation(DiffDays)`, even though the lowering for DiffDays already exists
  (`TemporalLowering.cs:82` → `Subtract`).
- No clock yields Duration/Time; JSON create stores raw strings (F32 family), and the printed
  factory (`Create(TimeSpan elapsed, …, TimeOnly opensAt)`) would throw if those strings
  reached it.

**Likely change:**
1. Fold `Date/DateTime − Date/DateTime` in expression parse/analysis into
   `DateOperation(DiffDays)` (temporal library owns the fold — extend the same seam as
   `DateExpr ± N`), and let analysis type it `Duration`/`TimeSpan`; the existing
   `TemporalLowering` DiffDays case then handles both VM and print.
2. Optionally allow bare-duration literals for `Duration`-typed targets (relax
   `TemporalMeaning.cs:98` when the target property is Duration-typed) so
   `assign Window to 90 Minutes` works.
3. Extend F32's coercion to parse `"00:30:00"` → `TimeSpan` / `"09:00:00"` → `TimeOnly` at
   the create boundary.
- Tests: assign-subtraction on VM + export; duration property populated and compared.

---

## Suggested sequencing after F1–25 lands

1. **F32 (+F15)** — self-contained MCP boundary coercion; highest user-visible value, low risk.
2. **F27** — small, surgical (dedupe the second registration in kept create bodies); C8 suites to guard.
3. **F34** — docs only; do it in any change touching the guide.
4. **F33** — fail-closed export guard, one conditional + tests.
5. **F31** — VM type resolution in create-argument position (small, shares the F26 area).
6. **F26** — trace-first, then the VM negative-literal/invoke-typing fix (coordinate with F1/F23 temporal work).
7. **F28** — shared-lowering notify-on-create (touches EffectLoweringPass + analysis plans; depends on F27's registration cleanup for correct counting).
8. **F29** — authorable stage-scoped policies or `require in Stage` (feature-sized; schedule alone).
9. **F35** — DiffDays fold + bare-duration relaxation (temporal feature; do after F1/F23/F26 temporal work settles).
10. **F30** — `sum` aggregate (largest feature; own task).
