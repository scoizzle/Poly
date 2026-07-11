# Micro-Task: E2E policy evaluation on the VM

**Parent**: WP5 / WS8  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [ ] Not Started  
**Pull when**: vertical slice needs runtime policy truth (or after WP6 if dogfood asks for EvaluatePolicy)

## Objective

Prove one complete path: V3 domain with a simple policy → lower → VM execute with a **C# record** argument → true/false.

## Context (current code)

- Bootstrap: `Poly.DomainModeling.Bootstrap.DomainFactory`
- Evolve: `DomainEvolution` / `EvolutionBuilder.AddPolicyToEntity(...)`
- Lower: `Poly.DomainModeling.Lowering.DomainExpressionLoweringPass`
- Eval helper: `PolicyEvaluator.CompileVMPredicate<T>` / `Evaluate` (dual LINQ+VM oracle OK)
- Execution: `Interpreter.Compile` / `Execute` / `SetArgs`
- Tests already cover raw DE → VM; this task attaches a **Policy on a domain entity** and evaluates via PolicyEvaluator or equivalent

## Exact Steps

1. Create domain via `DomainFactory.Create(...)` with an entity that has a numeric property (use builtin `Number` or map to int on a CLR record — match existing `PersonRecord` patterns in `DomainExpressionVmExecutionTests` if useful).
2. Attach policy: e.g. `Age >= 18` using `DomainExpression` comparison nodes + `AddPolicyToEntity`.
3. Compile/evaluate with `PolicyEvaluator.CompileVMPredicate<TEntity>` against records with Age=20 (true) and Age=10 (false). Prefer VM path; dual-oracle `Evaluate` is acceptable if already green.
4. Name test: `Policy_AgeGuard_EvaluatesOnVm` (or similar) under `Poly.Tests/DomainModeling/Lowering/`.
5. No domain-specific VM opcodes.

## Verification

- [ ] Build green
- [ ] New tests pass under TUnit
- [ ] No `Poly.Data.Modeling`
- [ ] No Trace/DebugHook dependency

## Out of Scope

- MCP EvaluatePolicy tool (optional follow-up micro-task if dogfood needs it)
- Contract interface generation
- Dictionary instance simulation (Interpretation owns later)
