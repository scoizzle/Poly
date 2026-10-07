# TASK A5b — Emit files and generator trees registered with references

Status: open. PR 123. Branch `slice/a5b-emit-file-trees` from master `bfe66beb`. Implementation `ddc61616`. Not merged.

Lane A. Review 2. Not on the V11 standing-merge list (decisions file: Scot hand-merges A5b). Plan: [`docs/domain-modeling/pipeline-convergence-plan.md`](../../../domain-modeling/pipeline-convergence-plan.md) **A5b**. Depends on A5a (merged `bfe66beb`). Does not touch PR 110 (C1a).

## Scope
`Emit` registers each per-entity `.cs` and `Poly.Types.cs` with a typed reference to the tree it printed. The Minimal API and DbContext contributors register the compilation unit they already build (`tree`) before printing, and the file artifact points at that tree. `demo.http` is a `file` with no reference. The allow-list is the test constant `AllowedWithoutTreeReference` (`demo.http` only). A reference whose path exists as another type fails `FindDanglingOrWrongType` as `WrongType`. Printed `*.cs.golden` and `demo.http.golden` stay byte-identical. `catalog.golden` gains the Emit `file` rows.

## Files
`Compile/DomainSession.cs`, `Compile/IArtifactContributor.cs` (`ContributedFile` references, `GeneratedTree`), `MinimalApiGenerator.cs` (host contributor), `DbContextArtifactContributor.cs`, `DslCompiler.cs` (register trees; do not append files Emit already returned). Tests: `EmitFileReferenceTests.cs`, `SliceCProducerLoopCatalogTests.cs`, `MinimalApiGeneratorTests.cs`, `EmitGolden/*/catalog.golden`.

## Done when
Every printed file artifact has a reference of the right type; the allowed-no-tree list is a test constant; printed goldens identical.

## SHIP / NOT SHIP
SHIP if a deliberately wrong-type reference fails the dangling check. NOT SHIP if text files can ship with no tree behind them and are unlisted.

Hand-edit: yes. The print sites still call `CSharpGenerator` on the same tree they register.

## Shape
- `file` may point at `entity`, `scaffolding`, or `tree`. `tree` is a `CompilationUnitNode` whose id path is the domain name plus the printed file name.
- Emit producer is `Emit`. Contributor producer stays the registered type name.
- `Lower` declares `file` and `tree` and registers neither, so Lower catalog text is unchanged.
- `DslCompiler` still writes every `file` artifact. It skips names `Emit` already returned so `Single` on file name keeps working.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-07 | implementer | Grok | `ddc61616` | PR 123 filed | filtered tests 150 passed, 0 failed. Printed `*.cs.golden` and `demo.http.golden` unchanged. `catalog.golden` lists Emit file rows. https://github.com/scoizzle/Poly/pull/123 |
