# ile-1 — Shrink or honor dishonest nodes

**Depends on:** ile-0  
**Files:** `DirectVmAbiEmitter*.cs`, analysis if compile-reject needs a diagnostic, `Poly.Tests/Interpretation/`

For each: **implement real VM semantics + tests**, or **fail closed at compile**, or **delete from executable language**. No passthrough.

| Node | Default (if unsure) |
|------|---------------------|
| `Comment` | Statement no-op; using as expression is compile-reject (never `0`) |
| `Await` | `GetAwaiter().GetResult()` on heap operand, BoxToAbi result; else reject |
| `TypeCast` | ABI convert using resolved CLR type (bitcast / unbox / Convert); miss → reject |
| `TypeAs` | Heap object `as T` → handle or 0 |
| `ParameterReference` | Resolve to `Parameter` or reject (no `0`) |
| `Default` | 0 / false / handle 0 by resolved type; unknown type → reject |
| `TypeOf` | Heap-allocate `Type` / `ITypeDefinition`; miss → reject |
| `ThrowExpression` | Throw the operand (same as statement) so it can appear in expressions |

Remove DomainResult wording from Invoke emit. `BoxToAbi` stays generic.

Each row: failing test first, then smallest production fix (ile suite §4).
