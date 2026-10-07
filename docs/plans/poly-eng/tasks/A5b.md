# TASK A5b — Emit files and generator trees registered with references

Status: implemented on `cursor/pipeline-convergence-full-send-b17a`. Lane A. Review 2. Plan A5b. Depends on A5a (merged `bfe66beb`).

## Scope
`Emit` registers each per-entity `.cs` and `Poly.Types.cs` as a `file` artifact pointing at its `entity` or `scaffolding` tree. DbContext and Program.cs register the compilation unit they print as a `host-tree` and point the file at it. `demo.http` is type `http-file`, the only entry in `PrintedArtifactRules.TypesAllowedWithoutTreeReference`.

## Done when
Every printed C# file artifact has one reference of type `entity`, `scaffolding`, or `host-tree`. The allow-list is a test constant (`http-file` only). `*.cs.golden`, Program.cs, demo.http, and DbContext goldens are byte-identical. `catalog.golden` lists the new `file` rows.

## SHIP / NOT SHIP
SHIP if a file pointing at `entity` when that path is only a `host-tree` is `WrongType`. NOT SHIP if a printed C# file can ship with no tree and is unlisted.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-07 | implementer | Grok | `20cad674` | implemented | EmitGolden 21 passed; SliceC 9; MinimalApi 33; LowerTrees 20; EmitFileReference 3. catalog.golden only. |
