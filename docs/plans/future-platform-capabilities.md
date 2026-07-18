# Future Platform Capabilities

**Date:** 2026-06-25  
**Revised:** 2026-07-18 (link domain extension experiment)  
**Status:** Ideas / deferred — none block the current IR pipeline or domain modeling work.

---

These are capabilities the platform will need as it matures beyond the compiler pipeline and into a usable product. They're recorded here to avoid losing them, not because they're urgent.

**Domain DSL/plugins:** Experimental research plan for extension packs (facets like `column("…")`, target exporters) lives in [`docs/experiments/domain-plugin-extension-platform.md`](../experiments/domain-plugin-extension-platform.md) — research later, not an execution queue.

## 1. Onboarding Path

Someone new opens the repo. What do they do?

- `FluentApiExample.cs` is fully commented out — not a reference.
- No quickstart, no "build a todo app in 5 minutes."
- No tutorial walking from domain description → IR → execution → C# output.

**Direction:** A `demo/` project (or a `Poly.Samples/` directory) with a single README-driven walkthrough. Start with "describe an Order entity," lower it, execute it, show the C# output. One file, one `dotnet run`, <5 minutes to value.

## 2. Persistence

Where does a domain model live between MCP sessions?

- JSON serialization of the domain model (`DomainModel` → JSON → `DomainModel`).
- Session save/restore via `Poly.Mcp`.
- Eventually: database-backed domain storage for long-lived projects.

**Current state:** Unknown. `Poly/Data/` and `Poly/Text/` exist but haven't been assessed for persistence readiness.

## 3. Versioning

Domains evolve. What happens to code compiled against an old model?

- An entity gains a stage, a policy changes, a property type shifts.
- Not the same as incremental compilation (which is about recompilation speed).
- Semantic compatibility: does code compiled against v1 of a domain still work against v2?
- Migration: can we auto-generate migration scripts from domain diffs?

**Direction:** Domain model diffs (already partially supported via the MCP tools) could drive compatibility checks and migration generation.

## 4. Domain-Level Testing / Simulation DSL

The VM validates that code executes correctly. But can a model author write:

> "Given `Order` in `Pending` stage, when `Confirm` action fires, expect `Order` in `Confirmed` stage."

No domain-level testing surface exists. Today you lower to IR, compile, execute, and inspect VM state manually.

**Direction:** A domain-level assertion language that lowers to the same IR and is validated by the VM. Example syntax: `Given(Order.InStage(Pending)).When(Order.Confirm()).Expect(Order.InStage(Confirmed))`. The `Given`/`When`/`Expect` triple maps cleanly to IR setup/execution/assertion blocks.

## 5. Documentation Generation

If the domain model is the source of truth, generate docs from it:

- Entity lifecycle diagrams (Mermaid state diagrams from stage definitions).
- Policy documentation ("`TotalMustBePositive` on `Order.Total`: value must be > 0").
- API surface documentation (generated C# interface docs from actions).

**Direction:** Another projection of the IR — same pattern as `CSharpCodeGenerator`, different target. A `MermaidGenerator` or `MarkdownGenerator` backend.

## 6. Observability

In production, traces should reference domain concepts:

> "`Order.Confirm` rejected: `TotalMustBePositive` policy failed (total was -$5.00)."

The plan mentions traceability via `NodeId.Source` on every `Instr`. An observability backend would:
- Map µop PCs back to domain constructs via the IR → AST → Domain chain.
- Emit structured logs (OpenTelemetry spans) with domain-level names.
- Enable policy-violation dashboards, action-latency histograms, stage-transition auditing.

## 7. Sandboxing / Capability System

If LLMs are authoring IR, what prevents malicious code?

- The VM has zero external dependencies in core (by design).
- But `Call` with `IsExternal=true` can invoke any CLR method.
- No capability system exists — no way to say "this macro can call `Math.Abs` but not `File.Delete`."

**Direction:** A `Capability` enum or allowlist on `Module` (or per-function). The `UopCompiler` validates external call sites against the capability set at compile time. Default: no external calls. Opt-in: `Math`, `String`, `Console` allowlists.

## 8. Package / Import System

The neurosymbolic vision says macros accumulate in a library and compose. But there's no module system:

- No way to say "import the `SieveOfEratosthenes` macro from the standard library, version 1.2."
- `Poly.Synthesis/` is a stub directory today.
- Macros need names, signatures, versions, provenance, and a registry.

**Direction:** A `MacroLibrary` that indexes `Module` objects by name and signature hash. Import is resolved by name → hash → `Module`. Versioning is hash-based (content-addressable), not semver. Provenance is metadata: which model discovered it, when, with what conformance suite.

---

## Relationship to Current Work

None of these block the IR pipeline (`docs/experiments/interpretation-compiler-framework-plan.md`). The pipeline is the foundation — having a working AST → IR → execute → generate loop makes all of the above concrete rather than speculative.

The one near-term action: an onboarding path (item 1) should ship alongside or immediately after the IR pipeline becomes default. A working compiler with no visible entry point is indistinguishable from a broken compiler.
