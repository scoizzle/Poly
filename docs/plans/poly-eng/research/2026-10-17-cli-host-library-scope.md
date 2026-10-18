# What a cli / bare-host library must provide

**Date:** 2026-10-17
**Status:** proposal / research. Not on the board `CURRENT` — nothing here is admitted work.
**Evidence:** [`docs/probes/findings/grep-2026-10-17.md`](../../../probes/findings/grep-2026-10-17.md)
(G4 no scalar result → no exit status; G5 no input door; G11 the contract seam cannot host the
algorithm) and its run log `docs/probes/grep-2026-10-17/LOG.md`.

## 0. Why we now know the scope

The grep probe built a complete text-tool *model* that runs on the interpreter and emits a host that
builds (`0/0`), and then had to drive it like this: `create_instance(Query)`, `AddSource`,
`LoadLine` × N, `Scan`, `Report` — seven calls for a three-line file, or one `POST` per line against
the generated API. Nothing in the language reads **argv**, a **file**, or **stdin**, and nothing
returns an **exit status**: the outcome exists only as a lifecycle stage the caller must interpret.

So a "cli/bare-host" door is the second half of making a model into a tool. It is a *host* concern,
not a domain-modelling concern — which is exactly why the seam has to be named carefully (see D3).

## 1. The seam (what "a door" is here)

`uses <id>` loads an `IDomainLibrary`; a door may also contribute **artifacts**. The precedent is the
`http` door, and its split is worth naming precisely because it decides D3:

- **in core**, `Poly/DomainModeling/Libraries/Http/HttpLibrary.cs` is *registration only* (an id, the
  door's seeds, an artifact contributor);
- **outside core**, the actual host emission lives with the other producers in `src/Poly.DslCompiler`
  (`MinimalApiGenerator` emits `Program.cs` + routes; `HttpFileGenerator` emits `demo.http`), and the
  `storage` door's `DbContext` follows the same shape.

A cli door would contribute a **process entry artifact** the same way: a program that reads input,
drives exactly one operation, writes results, and exits with a status.

Three precedents constrain the design:

- **`Main` in core is a hard no** (AGENTS.md frozen core), so the entry point is an *emitted* host,
  never a core `Main` — exactly how `http` emits `Program.cs`.
- **Poly.MCP is the harness, not the customer API** — so the CLI is a consumer artifact like the
  Minimal API generator, not a new surface on the harness.
- The **bound-contract seam is fail-closed and not registration-extensible** today (G11):
  `{Contract}Adapters` is a static class with no hook to inject an implementation, and its result
  conveys only success/failure. A CLI host must therefore run the domain's own operations rather than
  call out for them (the stub could be hand-edited, but not wired).

## 2. Required surface

**Input**

| Need | Why | Note |
|---|---|---|
| argv → action parameters | the tool's own arguments (`pattern`, flags) | typed by the action's declared parameters; a missing/extra/ill-typed argument fails closed at the door |
| **bulk ingest of a text stream** | the G5 gap: one call per line today | a declared mapping from a stream to rows of one entity — which property receives the line, which receives the ordinal, and what row-terminates a record. This is the single feature that makes the door usable for a text tool |
| a file argument and stdin | `grep x file` and `cat f \| grep x` | same ingest path; the door decides which |
| an *optional* structured input | machine callers | not required for S1 |

**Output**

| Need | Why | Note |
|---|---|---|
| the run's result rows in a deterministic order | grep prints matching lines in file order | the probe's hits live nested under each source (per-owner aggregates), so the host must flatten and order them — that flattening *is* part of the door's contract |
| **an exit status** | grep: 0 = matched, 1 = no match, 2 = error | see D2 — this is the probe's G4 |
| diagnostics on stderr, results on stdout | pipeability | fail-closed errors must not pollute stdout |

## 3. Decisions

- **D1 — how the ingest mapping is declared.** Candidates: a `uses cli` annotation pack
  (`table(...)`/`column(...)` are the precedent for projection metadata); an entity-level
  `uses cli` convention (the first `Text` property of the named entity gets each line); or a declared
  `contract` (weakest today: G11's adapters are a fail-closed stub with no registration hook, so a
  contract cannot carry the ingest). Recommend the projection-annotation shape, because storage
  already projects a domain to columns and this is the same idea to rows.
- **D2 — where the exit status comes from.** Two honest shapes:
  - **(a) scalar action return** (probe G4). The domain declares the status (`-> Number`) and the host
    passes it through. This is the *right* fix, and it is a language slice (the analyzer rejects
    non-entity returns while the emitter is already generic — see the dotted-up in the probe's G4).
  - **(b) a convention**: the host maps a *stage* (e.g. `Matched` / `NoMatch` / `Failed`) to a code.
    Smaller, no language change, but it hard-codes a meaning in the host that the domain cannot state.
    Recommend (a), with (b) as the interim only if the CLI door ships before the scalar slice.
- **D3 — is this a *library* in core, or a host template outside core?** The seam already has known
  multiplicity — `uses` doors plus their producers (http + storage, both in `ExtensionCatalog.Core`),
  with http shipping on a single consumer — so tenet 6 ("seam when multiplicity is known") does not
  argue against a third instance. What it *does* argue against is putting emission in core: the
  `http` split keeps `Poly/DomainModeling/Libraries/Http/` to registration while the generator lives
  in `src/Poly.DslCompiler`. So the honest reading is **(a) thin `uses cli` registration in core +
  the host emitter next to `MinimalApiGenerator`**, not a `scripts/`+`demo/` template — the latter is
  a sample, not the seam.
- **D4 — what "one operation" means.** grep runs one query per process. Should the host be
  "one root instance + one action" (the probe's shape: `Query.Scan`), or a small command table
  (`add-source`, `scan`, `report`)? Recommend the single-operation shape: it matches how the tool is
  actually used and keeps the door small.

## 4. Slices

| # | Slice | Exit criterion |
|---|---|---|
| S1 | host artifact + argv → action parameters; success/failure maps to 0/1 | a domain compiles with the door into a runnable program whose arguments reach the action and whose failure exits non-zero |
| S2 | bulk ingest of a text stream (lines → rows) | `prog pattern file` reads the file with **one** call, not one per line |
| S3 | exit status via D2 | the program's exit code distinguishes matched / no match / error, and (with D2a) the *domain* states it |
| S4 | deterministic result ordering + stdout/stderr split | output diffs clean against an expected file |

**The end-to-end exit criterion** (the reason this exists): a `grep`-like domain compiled with this
door runs as `prog <pattern> <file>` and **matches the system `grep -F` on a corpus**, differing only
where the text library's surface differs. That criterion needs both libraries; neither is shipping
alone.

## 5. Dependencies and what this library does *not* fix

- **Depends on the text library** ([`2026-10-17-text-library-scope.md`](2026-10-17-text-library-scope.md)):
  without `contains`, the tool is a schema for a text tool, and the exit-status slice (G4) is shared
  between the two notes.
- Does not fix the contract/adapter extensibility gap (G11); a CLI host runs the domain's own
  operations instead.
- Does not make the matcher expressible — that is the text library's `contains` (G1).
- Does not add regex, spans (`-o`), or `-A/-B/-C` context; those are text-library and model-shape
  questions respectively.
