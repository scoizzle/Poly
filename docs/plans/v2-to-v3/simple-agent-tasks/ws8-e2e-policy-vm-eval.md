# Micro-Task: E2E policy evaluation on the VM

**Parent Workstream**: WS8  
**Difficulty**: Medium (still small-model friendly if steps followed)  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started

## Objective

Prove one complete path: V3 domain with a simple policy → `DomainExpression` lower → `Interpreter.Compile` → execute with an entity argument → assert boolean/long result.

## Context You Need

- `docs/decisions/2026-06-08-domain-lowering-boundary.md` (domain → generic AST only)
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
- `Poly/DomainModeling/Lowering/PolicyEvaluator.cs` (if present — extend or mirror)
- `Poly/Interpretation/Interpreter.cs` — `Compile` / `Execute` / `SetArgs`
- Existing evolution or policy tests under `Poly.Tests/DomainModeling/`

## Exact Steps

1. Find how policies attach today (entity or stage `Policy` + `DomainExpression`).
2. Build a **minimal** domain in a test (builders or `DomainEvolution.Evolve()`):
   - One entity with one int/long property (e.g. `Age`).
   - One policy: `Age >= 18` (or equivalent with existing comparison `DomainExpression` nodes).
3. Lower the policy expression to Syntax AST via the existing lowering pass.
4. `Interpreter.Compile(node, CompilationMode.NoDebug)` then `Execute` with `SetArgs` (or the project’s current arg convention) supplying a matching CLR object or domain instance as used by `PolicyEvaluator`.
5. Assert true for Age=20, false for Age=10 (or long 0/1 ABI equivalents).
6. Name the test clearly: e.g. `Policy_AgeGuard_EvaluatesOnVm`.

## Verification

- [ ] `dotnet build` of solution or `Poly.Tests` succeeds
- [ ] New test passes under TUnit
- [ ] No domain-specific VM opcodes introduced
- [ ] Test does not depend on Trace/DebugHook

## Output

- One new test file or methods in an existing DomainModeling lowering/execution test class
- Brief note in your agent-summary if `PolicyEvaluator` needed a small fix

## Out of Scope

- Contract interface generation
- MCP tools
- Perf work
- New DomainExpression node kinds (use existing comparisons)
