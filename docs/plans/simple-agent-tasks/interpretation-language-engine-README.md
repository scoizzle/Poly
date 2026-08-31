# Interpretation as a generic language VM (`ile-*`)

**Status:** DONE 2026-08-31 — ile-gate closed (no POC passthrough, Compile fail-closed, LanguageVmTests + LanguageSurfaceTests)  
**Owning stream:** Interpretation only (`Poly/Interpretation/`, `Poly.Tests/Interpretation/`, CORE/Interpretation docs). **Do not mix** DomainModeling create/create-in, MCP, Grammar.  
**Prerequisite:** F1–F22 stabilization on `cleanup/interpretation-stabilization-review` (closed).  
**Authority:** VM is canonical semantics ([ADR](../../decisions/2026-06-08-vm-as-canonical-semantics.md)). Domain lowers to generic ops ([ADR](../../decisions/2026-06-08-domain-lowering-boundary.md)). Shipped ⊆ executable.

---

## Objective

Treat `Poly.Interpretation` as the **execution engine of a real interpreted language**, whose syntax is `Poly.Ast` nodes. DomainModeling is one **frontend** that lowers into that language. It is not a consumer the VM may special-case.

A legal program is a Syntax tree. Analyze → compile → execute on `VmState` is the whole meaning of that tree. LINQ and C# emit are projections, never oracles.

### End state (suite Done)

1. **One language.** Every *executable* Syntax node kind has honest VM semantics, or it is removed/rejected (CORE §5: do not ship a keyword whose implementation is a host escape).
2. **One compile door.** `Interpreter.Compile` fails closed on `DiagnosticSeverity.Error`. No lenient twin for “robustness.” Callers that want to inspect diagnostics use `Analyze` then decide.
3. **One oracle.** A shipped node’s meaning is proven by `Interpreter.Compile` + execute on that tree. `BuildExpression()` / LINQ / CFG-only tests are not sufficient.
4. **No domain in the VM.** Emitter, heap, ABI, and analysis must not mention `DomainResult`, `DomainEntityInstance`, or domain lowering. Invoke of an AST method is generic (`MethodInfo` or name+arity). `BoxToAbi` is the marshal table.
5. **Conformance suite** (`LanguageVmTests` / restored `VmParityTests`): one VM test (or explicit shrink) per executable node kind. Assumed invariants are asserts.
6. Full Interpretation + product suite green.

### Not this suite

- Domain create/create-in leaving EffectExecutor
- Teaching Domain callers to use the new compile door (separate DomainModeling PR after ile-1)
- A surface syntax besides the AST (no new `.poly` for general programs)
- Completing async (`Await` is shrink-or-implement, not a host passthrough)

---

## The language (what “interpreted programming language” means here)

| Role | Thing |
|------|--------|
| Source | `Poly.Ast.Nodes` trees (script = `Block` / expression; types = `TypeDefinitionNode`) |
| Compiler | `Interpreter.Analyze` + `DirectVmAbiEmitter` |
| Runtime | `VmState` (ring + heap handle 0 = null + `LoopTicks` sandbox) |
| Values | Stack scalar / bool 0\|1 / heap ref; float/double IEEE bits; decimal heap |
| Client | DomainModeling, algorithms, tests, future frontends |

If a node cannot be given those semantics, **shrink the language** (fail at compile / delete the node from the executable set). Do not leave a POC passthrough.

---

## Known lies vs a language VM (inventory 2026-08-25)

Dishonest emit (passthrough or dummy 0):

| Node | Today | Language-honest |
|------|--------|-----------------|
| `Comment` | ring `0` | No meaning: statement no-op, never a value (or reject as expression) |
| `Await` | operand passthrough; XML claims GetAwaiter | Implement GetAwaiter().GetResult() **or** compile-reject |
| `TypeAs` / `TypeCast` | operand passthrough | Real convert / type-as on ABI **or** compile-reject |
| `ParameterReference` | always `0` | Resolve to the parameter **or** compile-reject |
| `TypeOf` | C# only; VM `NotSupported` | Emit `typeof` handle **or** shrink |
| `ThrowExpression` | C# only; VM `NotSupported` | Same as `ThrowStatement` producing a value **or** shrink |
| `Default` | always `0` | Default of the resolved type (null handle vs 0 vs false) |
| `CompilationUnitNode` / type-def as program | not an entry | Script entry remains a `Node`; type-defs are analysis inputs, not `Main` |

API:

| Lie | Fix |
|-----|-----|
| `Compile` ignores Errors; `CompileChecked` is the real compiler | One door |
| ADR names `VmParityTests`; file is gone | Restore conformance suite as VM oracles |
| Many Interpretation tests only `BuildExpression()` | F21 as enforcement, not a README sentence |
| Emitter comment cites `DomainResult` | Generic “CLR/AST invoke result → BoxToAbi” |

---

## Locks (do not violate)

| ID | Rule |
|----|------|
| L1 | DomainModeling does not gain files in this suite |
| L2 | No new Interpretation abstraction without a second real use |
| L3 | Shrink > fake. Passthrough `Await`/`TypeCast` is not “good enough” |
| L4 | `Interpreter.Compile` on the same tree is the oracle |
| L5 | Assumed invariants are tests (header-visit loop limits, marshal table, handle 0, …) |
| L6 | C# emit may lag one slice if called out; VM never lags a shipped node |

---

## Tasks

| Task | Slice | DoD |
|------|--------|-----|
| [ile-0](./ile-0-language-contract.md) | Contract: CORE/README + compile door + inventory test | `Compile` = fail-closed; inventory test lists executable vs rejected kinds; CORE says language VM |
| [ile-1](./ile-1-shrink-or-honor.md) | Honor or reject: Comment, Await, TypeAs, TypeCast, ParameterReference, Default, TypeOf, ThrowExpression | No POC passthrough left; each has VM test or compile-reject test |
| [ile-2](./ile-2-conformance.md) | `LanguageVmTests`: every remaining executable kind via `Interpreter.Compile` | No Interpretation test whose *only* oracle is `BuildExpression` for a shipped executable node |
| [ile-3](./ile-3-functions.md) | Lambdas + `TypeDefinitionNode` methods as generic callables (no domain types) | Script with local functions / AST method invoke green on VM |
| [ile-gate](./ile-gate.md) | Pre-ship review + suite green + CORE honest | `[x]` only when 🔴🟠 closed |

ile-0 is the first admit. ile-1 is the language-honesty cut. Do not start ile-2 until ile-1 has no passthroughs.

---

## Done when

Someone can author a Syntax program (block, locals, loops, functions, objects, exceptions) with **no DomainModeling types in the tree**, compile it with `Interpreter.Compile`, and get the same fail-closed behavior a language VM owes: illegal trees rejected, legal trees executed, every construct tested on that path.
