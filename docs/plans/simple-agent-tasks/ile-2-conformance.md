# ile-2 — Language conformance suite

**Depends on:** ile-1  
Restore the ADR’s missing `VmParityTests` as `LanguageVmTests` (name for what it is).

- One `Interpreter.Compile` + execute (or compile-reject) case per executable node kind.  
- Grep gate: `Poly.Tests/Interpretation` files that `BuildExpression()` a shipped executable node must also `Interpreter.Compile` the same tree (or delete the LINQ-only test).  
- Keep LINQ generator tests that test **the LINQ generator**, not language meaning.
