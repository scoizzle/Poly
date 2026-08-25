# ile-2 — Language conformance suite

**Depends on:** ile-1  
Restore the ADR’s missing `VmParityTests` as `LanguageVmTests` plus same-tree VM↔LINQ parity.

The LINQ expression path is **not** a second language and is **not** disposable. It exists to:

1. **Validate VM semantics programmatically** — the same Syntax tree runs on `Interpreter.Compile` and on `BuildExpression` / `LinqExpressionGenerator`; results must match (after ABI normalize: bool 0/1, ints as long, float/double bits).
2. **Inspect execution** — the expression tree is a readable stand-in for what the program does, useful when debugging the custom VM.

Canonical meaning is still the VM. When they disagree, fix the VM (or shrink the node). When the VM fail-closed rejects an illegal tree, LINQ may still build — that is not a VM bug; assert compile-reject on the VM side.

- One `Interpreter.Compile` + execute (or compile-reject) case per executable node kind (`LanguageVmTests`).
- Language-meaning tests that `BuildExpression()` a shipped executable node must also `Interpreter.Compile` the **same** tree and assert matching results (or VM compile-reject).
- Keep tests that only exercise the LINQ **generator** (`LinqExpressionGeneratorTests`) as generator tests.

## Landed

Language-meaning `BuildExpression` tests in Arithmetic/Modulo/UnaryMinus/NumericPromotion/Constant/Parameter/Block/BlockScope/Conditional/Coalesce/ForEach/TypeCast/New/Lambda/InterpreterIntegration now also `Interpreter.Compile` the same tree (or assert VM compile-reject). `LanguageVmTests` covers additional executable kinds.

## Remaining VM↔LINQ mismatches

Closed: numeric promotion (IEEE mix + decimal + uint/ulong), string `Add`, optional ctor defaults, lambda outer `Parameter` capture / inline bind, nullable coalesce (`int?` heap-boxed so `0` ≠ null).
