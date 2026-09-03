# Fleet-eval 2026-08-12 — slice 12: MCP surface

Slice: `Poly.Mcp/` (Tools/DslTool, RuntimeTool, PolicyTool, OracleTool, EvolveTool/QueryTool/SessionTool; Sessions/McpSessionStore; Program.cs; `Poly.Mcp/Docs/poly-dsl-guide.md` product doc).

Probes (all run through `scripts/run-probe.sh`, 0/0 gate unless noted):
- `probes/fleet-eval/12-mcp/mcp-library.poly` — 0/0
- `probes/fleet-eval/12-mcp/mcp-orders.poly` — 0/0
- `probes/fleet-eval/12-mcp/mcp-edge.poly` — 0/0
- `probes/fleet-eval/12-mcp/mcp-create-defaults-fail.poly` — **fails codegen** (finding F1 repro)
- `probes/fleet-eval/12-mcp/mcp-omit-constrained-props.poly` — 0/0 (finding F3 repro; divergence is behavioral)

Environment note: `Poly.Tests/Mcp/FleetEvalRuntimeProbeTests.cs` (a sibling agent's in-progress file, last touched 22:42) currently breaks the `Poly.Tests` build (CS1061 `Bool_IsTrue_Assertion` / `.OrFail`), so live MCP-tool verification via the TUnit suite was blocked; runtime claims below are code-verified against `DomainEntityInstance` / `DomainInstanceStore` / the tool implementations, and the DslCompiler path (`DomainProgramProjection` → `CSharpGenerator`, which shares the `DomainToCSharpExporter` lowering) was exercised per probe.

---

## F1 — `create`/`create in` binding a defaulted prop + sibling `default(now/today/guid)` fails codegen (🔴)
- **Signal:** compile-fail (late-rung; analysis accepts, codegen throws) + export/runtime divergence (runtime path works)
- **Severity:** 🔴
- **Slice:** MCP surface (apply_dsl accepts; `export_domain_to_csharp` / DslCompiler throw)
- **Repro:** `probes/fleet-eval/12-mcp/mcp-create-defaults-fail.poly` + `scripts/run-probe.sh`:
  `Box { Label: Text required length(2,20); Qty: Number range(0,150) default(0); CreatedAt: DateTime default(now) }`,
  `Pallet.Load: action -> Box { create in boxes { Label: "BX" Qty: 10 } }`
  → `Code generation failed: default(now) on property 'CreatedAt' (type 'DateTime') cannot be lowered: 'now' is not a member of an enum that 'CreatedAt' is typed with.`
- **Expected:** guide §3 `default(value)` + §12 ("`now`/`today`/`guid` are authorable in `default(...)`") + §9 `create in` → compiles 0/0; the created Box fills `CreatedAt` via `DateTime.UtcNow`. The runtime (MCP `invoke_action` → `DomainEntityInstance.CreateChildInstance`) handles it via `EvaluateDefaultValue`.
- **Actual:** `EffectLoweringPass.AppendDefaultedPropArgs` calls `DomainToCSharpExporter.LowerDefaultConstantNode` unconditionally; that method throws for a `PropertyAccess` default that isn't an enum member, so the intended `runtimeExpr is not null → Constant(null) sentinel` branch (EffectLoweringPass.cs:704) is unreachable. Trigger: any create/create-in that binds **one** defaulted property (here `Qty`) while the target entity has **another** defaulted property using a runtime keyword. `create in boxes { Label: "BX" }` alone (no defaulted prop bound) compiles fine — a hard-to-see dependence on the binding set.
- **Proposed patch:** guard the call: `var defaultNode = runtimeExpr is null ? DomainToCSharpExporter.LowerDefaultConstantNode(...) : null;` (or make `LowerDefaultConstantNode` return null for `now/utcnow/today/guid`, matching its own doc comment).

## F2 — subscription `create Type` effect is silently dropped in the export (🟠)
- **Signal:** silent gap (guide §0.4, §7 patterns)
- **Severity:** 🟠
- **Slice:** MCP surface — export (`export_domain_to_csharp` / DslCompiler) vs runtime (`create_instance` + `invoke_action` subscription fan-out)
- **Repro:** `probes/fleet-eval/12-mcp/mcp-library.poly` — `when loans Overdue as loan { create Fine { Amount: (loan Amount); ... } }`. Generated handler:
  ```csharp
  internal void WhenEachLoanOverdue(Loan loan) {
      var fineResult = Fine.Create("Overdue", loan.Amount);
      ...
      var fine = fineResult.Value;   // ← discarded
  }
  ```
- **Expected:** the runtime materializes the Fine (added to the subscriber's `CreatedChildren` and the store via `CreateChildInstance`); the export must not silently drop a created entity (fail loud or wire it into `_fines`).
- **Actual:** the Fine is created then discarded — no observable state change in the export (the `Fine` factory `CreateFines` that would add to `_fines` exists but is unused). Export/runtime divergence on the same DSL.

## F3 — create-in omitting a non-defaulted constrained string prop: export throws, runtime stores null (🟠)
- **Signal:** export/runtime divergence + silent constraint bypass
- **Severity:** 🟠
- **Slice:** MCP surface — `apply_dsl` + `invoke_action` (runtime) vs `export_domain_to_csharp` (export)
- **Repro:** `probes/fleet-eval/12-mcp/mcp-omit-constrained-props.poly`:
  `Box { Code: Text pattern("^[A-Z]{2}[0-9]{3}$") }`, `Pallet.Load: action -> Box { create in boxes { } }`
  → export emits `CreateBoxes("")` → `Box.Create("")` → pattern `Regex.IsMatch("", ...)` fails → `InvalidOperationException`; runtime `DomainEntityInstance.Create` leaves `Code=null` and `ValidateConstraints` skips the pattern branch for non-strings → `Load` succeeds storing a null `Code`.
- **Expected:** analysis should reject a create that omits a constrained non-defaulted property the export cannot express (or both paths must fail identically). The guide says `pattern(regex)` validates stored values at write time.
- **Actual:** divergent: export throws a raw exception (not even a `DomainResult.Failure`), runtime silently stores a pattern-violating null.

## F4 — guide §0.4 semicolon-separated create initializers are rejected by the shipped parser (🟠)
- **Signal:** guide drift
- **Severity:** 🟠
- **Slice:** MCP surface — product doc `poly-dsl-guide.md` vs shipped `apply_dsl` parser
- **Repro:** `probes/fleet-eval/12-mcp/mcp-guide-s4-semicolon.poly` (the guide's exact §0.4 example: `create Fine { Amount: 5; Reason: "Overdue" }`) + `run-probe.sh` → `Parse error: Unexpected character ';'`. Whitespace-separated form (`Amount: 5 Reason: "Overdue"`) parses.
- **Expected:** the product-true guide's examples must apply through `apply_dsl`.
- **Actual:** only whitespace-separated initializers are accepted; the `;` form in the guide is a parse error — an agent following the golden workflow (guide → `.poly` → `apply_dsl`) is stopped.

## F5 — guide §0.3 "to-one navigation bindings … remain rejected" contradicts the shipped analyzer and the guide's own ✅ example (🟠)
- **Signal:** guide drift
- **Severity:** 🟠
- **Slice:** MCP surface — product doc vs `apply_dsl` analyzer
- **Repro:** `mcp-library.poly` uses the guide's ✅ `create in loans { book: book }` and compiles 0/0; `Poly.Tests/DomainModeling/ActionEntityReturnTests.Analyze_CreateInWithSingularNavBinding_NoError` asserts no "unknown property 'book'" error.
- **Expected:** the stale sentence "To-one navigation bindings in `create in` initializers remain rejected (analyzer fail-closed)" (guide lines ~90–91) should be removed/corrected.
- **Actual:** the product-true doc contradicts both the shipped behavior and its own canonical example.

## F6 — entity-level store-dependent policies make every action on the entity throw in the export (🟡)
- **Signal:** fail-loud-but-sharp (dead-ends a whole surface)
- **Severity:** 🟡
- **Slice:** MCP surface — `export_domain_to_csharp` / standalone export
- **Repro:** `probes/fleet-eval/12-mcp/mcp-orders.poly` — Order declares entity-level `AllLinesShipped: policy { all lines where ... }` and `OpenLineCount`. Every generated action (`Submit`, `ShipAll`, `Pay`) calls these as entity-level guards, and each lowers to `throw new NotSupportedException("... requires store-aware evaluation ...")` — so **no action on Order can ever run** in the standalone export, including `Pay` which only touches `invoice`.
- **Expected:** the guide documents store-dependent policies as runtime-only, but not that an entity-level store-dependent policy amplifies to block every action (a `require`-gated local action should still be callable). Runtime (MCP store) returns a clean policy-blocked Failure; export throws.
- **Actual:** whole-entity surface dead-ends with a raw throw.

## F7 — fractional JSON numbers are silently truncated to long in MCP value conversion (🟡)
- **Signal:** silent gap (silent data corruption)
- **Severity:** 🟡
- **Slice:** MCP surface — `create_instance`, `invoke_action` args, `evaluate_policy` bag, `simulate_policy` (RuntimeTool/PolicyTool/OracleTool share the same `JsonElement`→CLR converter: `JsonValueKind.Number => (long)je.GetDecimal()`)
- **Repro:** `create_instance(sessionId, "Widget", """{"Price": 29.99}""")` on `Price: Number` → stores 29 with no error; `evaluate_policy(..., properties: """{"Total": 29.99}""")` → evaluates with 29.
- **Expected:** `Number` maps to Int64 in both runtime and export; a fractional input should be rejected loudly ("expected integer") rather than silently truncated.
- **Actual:** silent precision loss; the caller's value is never validated against the target property type.

## F8 — `invoke_action` does not validate args against the action's declared parameters (🟡)
- **Signal:** fail-loud-but-sharp / reliability
- **Severity:** 🟡
- **Slice:** MCP surface — RuntimeTool.InvokeAction
- **Repro:** action `Add: action (delta: Number) { assign Qty to Qty + delta }`; `invoke_action(args: """{"nope": 5}""")` — the unknown key is injected into the value bag and `delta` is absent; no "unknown parameter 'nope'" / "missing parameter 'delta'" diagnostic at the tool boundary.
- **Expected:** validate provided keys against the resolved action's `Parameters` (names + requiredness), fail loud with the action's declared signature.
- **Actual:** misspelled/missing args flow into the VM as injected bag values (silent no-op or opaque VM failure).

## F9 — created children are double-registered in the instance store after `invoke_action` (🟡)
- **Signal:** silent gap (duplicate subscription dispatch)
- **Severity:** 🟡
- **Slice:** MCP surface — RuntimeTool.InvokeAction child registration
- **Repro:** any action with `create`/`create in` (e.g. `mcp-library.poly` `CheckOut` at runtime): `CreateChildInstance` already calls `Store.Add(child)` (sets `child.Store`); `RuntimeTool.InvokeAction` then re-`Add`s each new child (`st.InstanceStore.Add(child)`), and `DomainInstanceStore.Add` does not dedup → the child appears twice in `_instances`.
- **Expected:** single registration.
- **Actual:** duplicate `_instances` entries; `NotifyTransition` iterates `_instances`, so a child that itself has subscriptions fires subscriber effects twice.

## F10 — session isolation: `list_sessions` exposes every session and no tool enforces ownership (🟠)
- **Signal:** security
- **Severity:** 🟠
- **Slice:** MCP surface — Sessions/McpSessionStore, SessionTool/QueryTool/EvolveTool/RuntimeTool
- **Repro:** on the shared MCP server, `list_sessions` returns every agent's `sessionId` (static `ConcurrentDictionary`, no per-agent binding); any agent can then call `apply_dsl` (which **replaces** the domain), `get_domain_analysis`, `get_instance`, `link_instances`, etc. against another agent's session ID.
- **Expected:** sessions scoped to the owning agent (or an explicit share mechanism); at minimum, read-only tools shouldn't enumerate other sessions.
- **Actual:** full cross-agent read + destructive write access to any session whose ID is discovered via `list_sessions`.

## F11 — `create_domain_session` with an empty/whitespace name throws an uncaught exception (🟡)
- **Signal:** fail-loud-but-sharp (tool envelope violated)
- **Severity:** 🟡
- **Slice:** MCP surface — SessionTool.CreateDomainSession / McpSessionStore.Create
- **Repro:** `create_domain_session(domainName: "")` → `McpSessionStore.Create` → `DomainFactory.Create("")` → `ArgumentException` (ThrowIfNullOrWhiteSpace) escapes the tool (no try/catch, no `DomainToolResponse`).
- **Expected:** a clean `DomainToolResponse(Success: false, ...)` like every other input-validation path in the same file.
- **Actual:** raw exception surfaces to the MCP client.

## F12 — a path-prefix value followed by another initializer fails to parse without parentheses (🟡)
- **Signal:** fail-loud-but-sharp (confusing parse error)
- **Severity:** 🟡
- **Slice:** MCP surface — `apply_dsl` parser (initializer grammar)
- **Repro:** `create Fine { Amount: loan Amount Reason: "Overdue" }` → `Parse error: Expected property name, got ':'`. The `DslExpressionParser.ParseRelatedAccess` path-continuation (line ~189) greedily consumes the next initializer's name; the `InPropertyInitializerValue && Peek(1)==Colon` boundary guard is only in `ParsePrimary`, not the recursive hop. `Amount: (loan Amount) Reason: "Overdue"` works.
- **Expected:** guide §7 permits peer path-prefix "in initializers"; the whitespace form should parse (or the error should point at parentheses).
- **Actual:** opaque parse error with no hint; an agent copying a natural form is stopped.

---

## Lens summary (ranked)

**quality:** F1 (🔴), F3 (🟠 divergence), F2 (🟠 silent), F7 (🟡), F8 (🟡), F9 (🟡), F12 (🟡)
**consistency (vs guide):** F4 (🟠), F5 (🟠)
**product:** F1 dead-ends the golden apply_dsl→export_domain_to_csharp workflow; F6 (🟡) dead-ends entity surfaces with store-dependent entity-level policies; F2/F3 are guide-endorsed patterns with divergent exports
**security:** F10 (🟠), F11 (🟡)
**reliability:** F1 (late-rung codegen crash instead of analysis rejection), F3 (export throw), F8, F9, F11; otherwise invalid inputs fail closed cleanly (unknown `add`/`remove` kinds, bad JSON, unknown entity/instance/relationship, reversed link ends, wrong entity types — all verified in tool code)
