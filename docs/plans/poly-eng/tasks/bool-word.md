# TASK bool-word — a boolean is an inline word

Status: in review
Branch `slice/bool-word`. PR #246. Lane A. Review 1. Implement mill: OpenCode (review on the opposite mill).

Admission: Scot-directed 2026-10-20 ("take care of the boolean-ness right now"). Independent of PR #221; based on `origin/master` after ABI2–4 and `RingCodec`.

## Scope

Remove the boolean storage class from the VM representation model. The ring is one
long word per value; a boolean is 0 (false) or non-zero (true). Boolean-ness is the
CLR type (`typeof(bool)`), read at decode boundaries, not a slot kind.

- `ValueRepresentationKind.Bool` is removed; `StackScalar` documents the inline word.
- `RingCodec` classifies `bool` as inline; the word→bool decode (`!= 0`) stays.
- Every former `StackScalar or Bool` consumer treats `StackScalar` only.
- A boolean root renders `true`/`false` from `VmProgram.RootClrType`, not the kind.

Not in scope: `bool?` heap-cell storage (already heap-resident and unchanged), and
the pre-existing scalar-to-`object`-variable boxing gap recorded in review F5.

## Files

- `Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs` — enum + classifier.
- `Poly/Interpretation/Analysis/Semantics/SyntaxTypeCompatibilityAnalyzer.cs` — classifier.
- `Poly/Interpretation/Vm/RingCodec.cs` — classification and decode.
- `Poly/Interpretation/Vm/DirectVmAbiEmitter*.cs` — paired arms.
- `Poly/Interpretation/Interpreter.cs`, `Poly/Interpretation/CSharp/CSharpScript.cs` — root dispatch/rendering.
- Tests: `RingCodecTests`, `ValueRepresentationTests`, `InterpretResultIntegrationTests` (kind assertions updated with rationale).
- Docs: `Poly/Interpretation/README.md`, `Analysis/README.md`, `docs/interpretation/vm-abi-reference.md`.

## Done when

- No production reference to `ValueRepresentationKind.Bool`.
- `KindFor(typeof(bool))` is `StackScalar`; `IsInline(typeof(bool))` true; `TryInlineConstant(true)` is 1; `ToRing(true/false)` is 1/0.
- Boolean roots render `true`/`false`; `bool`/`int` cross-assignment still fails `VmTypeCompatibility`.
- Full suite green; `Poly.Benchmarks` builds.

## SHIP if

Build clean, changed-area tests green, no 🔴/🟠, full suite recorded green on the tip.

## NOT SHIP if

A boolean takes a different storage path, a boolean root renders `1`/`0`, or a consumer
that needed boolean-ness loses it.

## Tests

`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/<Class>/*'` per class, then the full suite.

## Hand-edit

The rule is one enum member removed and one line in `RingCodec`; a reader can change a
classification or the root rendering without an agent.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-20 | OpenCode | — | `df8e87ce` | implement; test-first red recorded (Expected StackScalar, got Bool) | full suite 4153 passed |
| 2026-10-20 | OpenCode | — | `eff1aa6e` | verifier COMPLETE; 2 doc nits fixed | — |
| 2026-10-20 | OpenCode | — | `d3f010c2` | second opinion COMPLETE (mimo); 3 doc/comment nits fixed; 1 pre-existing observation (F5, out of scope) | — |
