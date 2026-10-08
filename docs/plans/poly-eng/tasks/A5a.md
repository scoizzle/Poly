# TASK A5a — Contributors return artifacts

Status: merged #122 `bfe66beb` 2026-10-07.

Lane A. Review 2. Not on the V11 standing-merge list (decisions file: Scot hand-merges A5a). Plan: [`docs/domain-modeling/pipeline-convergence-plan.md`](../../../domain-modeling/pipeline-convergence-plan.md) **A5a**. Depends on A3a (merged). Does not touch PR 110 (C1a).

## Scope
`IArtifactContributor.Contribute` returns artifacts instead of `(fileName, text)` tuples. Four implementers: `DbContextArtifactContributor`, `MinimalApiHostArtifactContributor`, `TrackingContributor`, `HelloContributor`. `DslCompiler` registers those artifacts and writes the text files from the catalog. The source-text grep in `SliceC_HttpAndPersistence_HostFilesComeFromProducerLoop` is a behavioral catalog test: `demo.http` and the DbContext artifact come from the registered contributors. T0 goldens, including DslCompiler outputs, stay byte-identical.

## Files
`Compile/IArtifactContributor.cs` (`ContributedFile`), `Compile/DomainSession.cs`, `DbContextArtifactContributor.cs`, `MinimalApiGenerator.cs` (contributor class), `DslCompiler.cs`; tests `SliceCProducerLoopCatalogTests.cs`, `DslCompilerArtifactContributorTests.cs`, `MinimalApiGeneratorTests.cs`.

## Done when
T0 golden (including DslCompiler outputs) identical; the behavioral catalog test replaces the grep test.

## SHIP / NOT SHIP
SHIP if golden is identical and no test reads product source as text. NOT SHIP otherwise.

Hand-edit: yes. Public interface change; the four implementers are listed above.

## Shape
- Type `file`. Id path is domain name plus file name. Payload is the file text. No references (A5b adds trees).
- Lower and Emit declare `file` and leave it empty, so catalog text is unchanged. The compiler registers contributed files after Emit.
- The catalog producer is the registered contributor's type name. `DslCompiler` writes every `file` artifact from that catalog. Entity `.cs` files stay Emit tuples until A5b.
- N2 can move HTTP registration into `HttpLibrary` without editing this test. The test checks producer names and catalog bytes, not where `AddArtifactContributor` is called.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-07 | implementer | Grok | `5072c4c6` | PR 122 filed | filtered tests 98 passed, including Emit golden and CRM DslCompiler host files |
| 2026-10-07 | verifier | Grok | `71fce505` | verify pass; PR 122 left open | severity none. Filtered tests 98 passed, 0 failed (SliceCProducerLoopCatalogTests, DslCompilerArtifactContributorTests, MinimalApiGeneratorTests, EmitGoldenTests, DslCompilerCompileOracleTests, DslCompilerSqliteOracleTests). Build & test SUCCESS on `71fce505`. Not squash-merged. |
| 2026-10-07 | restructure | - | `bfe66beb` | merged #122 | squash-merged to master |
