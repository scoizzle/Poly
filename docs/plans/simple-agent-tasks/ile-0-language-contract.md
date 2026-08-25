# ile-0 — Language contract

**Suite:** [`interpretation-language-engine-README.md`](./interpretation-language-engine-README.md)  
**Files:** `Poly/Interpretation/Interpreter.cs`, `Poly/Interpretation/README.md`, `docs/CORE.md` (Interpretation rows only), `Poly.Tests/Interpretation/`  
**Does not:** change DomainModeling callers (they keep compiling until a later Domain PR maps to the one door).

## Goal

Name Interpretation as a **generic language VM**. Make the compile API match that: illegal programs do not emit.

## Work

1. **CORE + Interpretation README**  
   - Interpretation executes Syntax programs. DomainModeling is a client.  
   - Pass count/order already asserted by `StandardAnalyzer_PassNames_MatchInterpreterPipeline` — README must not still say “12 passes” or a Use*-list order that `AnalyzerBuilder` does not build.  
   - Delete “lenient Compile for robustness” as a product story.

2. **One compile door**  
   - `Interpreter.Compile(...)` runs `FailLoudOnAnalysisErrors` (same as today’s `CompileChecked`).  
   - Keep `CompileChecked` as an obsolete alias **or** delete it in this slice if call-site count in Interpretation tests is small; Domain still compiling via `Compile` then gets fail-closed “for free” — **verify with full suite**, not by editing Domain files unless a test fails.  
   - If a Domain test fails because it relied on silent coerce, that is in-scope to **fix the test or the tree**, not to restore a lenient Compile. Prefer fixing the test in `Poly.Tests` without Domain production edits; if production Domain must change, stop and split a Domain PR.

3. **Inventory test** (`LanguageSurfaceTests` or similar)  
   - Table: node type → `Executable` / `CompileReject` / `AnalysisOnly`.  
   - Executable: `Interpreter.Compile` does not throw `NotSupportedException`.  
   - CompileReject: emit or analyze fails loud (named exception or diagnostic).  
   - Seed from `DirectVmAbiEmitter.CompileNodeInner` + known passthroughs (ile-1 will move passthroughs into CompileReject or Executable-honest).  
   - ile-0 may mark `Await`/`TypeCast`/… as `Dishonest` in the table with `Assert.That` documenting current passthrough — **or** skip those rows until ile-1. Prefer a `Dishonest` row that **fails** if someone “fixes” silently without a VM oracle (optional). Simplest ile-0: inventory of kinds the switch handles vs default NotSupported.

4. **Invariant tests already landed stay.** Do not weaken MaxLoopIterations / Heap / marshal tests.

## DoD

- [x] `Compile` fails on `DiagnosticSeverity.Error` (test: unresolved member, TH0001 static this).  
- [x] README/CORE: language VM, not domain helper; compile door is one.  
- [x] Inventory test exists.  
- [x] Full `dotnet run --project Poly.Tests/Poly.Tests.csproj` green.  
- [x] No DomainModeling production files unless a green-suite failure forces a split.

## Out of ile-0

Honor/reject of Await, TypeCast, Comment, TypeOf, ThrowExpression (ile-1). LINQ-only test migration (ile-2).
