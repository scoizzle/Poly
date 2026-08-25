# Anti-Pattern 007: Single-Point Production Dependency

**Problem:** The entire V2 domain modeling system (~56,000 lines) has exactly one production consumer: `Poly.Mcp/DomainTools.cs`. Benchmarks and tests exercise it, but the MCP server is the sole path that ships. This concentrates risk — any refactoring, simplification, or migration must not break the MCP contract, and there's no second production consumer to validate changes against.

## Plan

1. **Build a command-line domain validation tool.** A small CLI that loads a domain model, runs the full analysis pipeline, and exits with a summary of diagnostics and model statistics. This exercises the same paths as MCP but independently and on-demand.

2. **Create benchmark profiles for the MCP's domain mutation path.** The `DomainMutationIntentEngine` → `Mutation.Apply()` → `DomainModelAnalyzer.Analyze()` chain is the MCP's critical path. A benchmark that exercises this end-to-end gives a second signal that changes to domain modeling are safe.

3. **Add integration tests that exercise the full lowering pipeline** from domain model → `DomainLoweringGenerator` → `Lowering.Lower` → `Vm.Execute`. This validates the complete code path without needing the MCP server running.

4. **Document the MCP contract surface explicitly.** Define which types and methods constitute the MCP contract (`DomainMutationIntent` subtypes, `DomainMutationIntentEngine`, the analysis pipeline). This makes it explicit which code is under the highest change scrutiny.

**Timeline:** 1-2 weeks for the CLI tool and benchmarks. Documentation is ongoing.

## Risk Reduction

These steps don't eliminate the single-point dependency — MCP is still the only shipping consumer. But they provide independent signals that changes are safe. A CLI tool catches breakage. Benchmarks catch regressions. Integration tests validate the full pipeline. Documentation makes the contract explicit rather than implicit.
