# Round 5 — agent-c findings (slice: Q3′ policy quantifiers + path-prefix multi-hop + owned + `Rel exists` + store-dependent `for` predicates)

Probes: `probes/round5-agent-c/` — loanbook.poly (multi-hop to-one + Q3′), fulfillment.poly
(local-policy `for`), inventory.poly, loanbook-bad.poly / fulfillment-bad.poly (reject probes).
Exports statically reviewed; runtime via MCP session b1f2a566.

## F11 — store-dependent policies and `for` predicates: runtime supports, export dead-ends, and the guide never documents the boundary
- **Signal:** guide-drift (export/runtime divergence with no documented boundary)
- **Severity:** 🟠
- **Slice:** Q3′ quantifiers / path-prefix / exists / store-dependent `for` predicates
- **Repro:**
  - `for Rel as x where x <quantifier-or-path-prefix-policy> invoke x.Action` →
    analysis error: `ForEachInvoke predicate policy 'X' is store-dependent (quantifiers / path-prefix / exists) and cannot be compiled to standalone C#. Use a local policy over the record's own properties.` (`probes/round5-agent-c/fulfillment-bad.poly`, `probes/round5-agent-a/reorder-engine.poly` BadStorePredicate).
  - Q3′ policies on an entity (any/all/none/count, `Rel exists`) compile to
    `public bool HasBigLoan() { throw new NotSupportedException("Policy 'HasBigLoan' requires store-aware evaluation and cannot be compiled to standalone C#."); }` — fail loud only when CALLED; any action `require`-gating them is permanently un-runnable in the export (`probes/round5-agent-c/loanbook.poly` lines 72–103).
  - Runtime supports both: evaluate_policy(instanceId=…) with linked instances returns
    correct quantifier/path-prefix results (verified: path-prefix to-one policies
    `IsClassic`/`LongBook` compile to real gated C# and runtime reads store links).
- **Expected:** the guide's "Shipped-surface boundaries" section must document that
  store-dependent expressions are runtime-only (MCP store path) and that the C#
  export rejects them (analysis error for `for` predicates; NotSupportedException
  for policy methods). Without that, authors follow the guide (which documents the
  store+link runtime path and the `for` predicate rule as "named policy") and dead-end
  in the export.
- **Actual:** guide contains no mention of "store-dependent", "standalone", or the
  export's NotSupportedException boundary (verified by grep). Silent-to-the-guide
  asymmetry.
- **Proposed patch:** document the boundary in §6 fan-out and §8 shipped-surface
  boundaries (the export throws/analyze-rejects store-dependent expressions; the
  runtime store path is the supported evaluation surface). Optionally: analyze-time
  error for entity policies that are store-dependent AND referenced by a `require`
  gate / export use, so the failure surfaces at authoring instead of first call.

## Verified-OK in this slice (not findings)
- Reject edges fail loud and precise: quantifier on OneToOne (DMSS003-style),
  bare path-prefix on `many`, reverse-side path-prefix naming the real source,
  self-relationship quantifier, `or` inside quantifier bodies.
- To-one path-prefix (single-hop and multi-hop) compiles to fail-loud C#
  (`(this.Book ?? throw new InvalidOperationException(...)).Title == "Classic"`) and
  matches the runtime store path.
- Empty-set semantics for `any`/`all`/`none`/`count` match the guide's table on the
  runtime path; `count` in arithmetic (scalar) is correct.
- Local-policy `for` predicates (fulfillment.poly) compile and behave.
