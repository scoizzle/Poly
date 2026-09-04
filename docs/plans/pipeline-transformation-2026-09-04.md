# Pipeline transformation (named stages)

**Date:** 2026-09-04  
**Status:** P1–P5 executed 2026-09-04. **Not CURRENT, not a suite.** P6 (clocks) is an open argument — do not implement.  
**Frozen:** [`docs/CORE.md`](../CORE.md) §0 · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`AGENTS.md`](../../AGENTS.md) **Frozen core**

P1–P5 landed on the product path (`session.Lower`, one Create/CreateIn/EnsureUnique tree, compile-once action bodies, HTTP fail-closed against the module, analysis cache binds the authoring session). P6 clocks left for argument. Still **not CURRENT** — do not invent a second CURRENT.

---

## Why

The frozen core is clear. The **running** pipeline is not: lower happens twice with a flag, simulate re-lowers from Effect IR, `DomainProgramProjection` is a façade over the C# exporter, host files walk `Domain` beside the module, `RuntimeAnalysisCache` reopens a core-catalog session.

We want a pipeline that **makes logical sense** — named stages, one product per stage, consumers only at the end — then transform the current implementations onto it. Hosts (scratch store, C#, HTTP, later CLI) stay replaceable.

---

## Target pipeline (six stages)

One session. Left-to-right. Each stage has one job. Later stages do not re-do earlier ones.

```text
0 Parse     source + session language     → Domain (facts)
1 Load      Domain.uses                   → same session (already loaded for parse)
2 Analyze   Domain                        → AnalysisResult (bags + replacements on Domain nodes)
3 Lower     Domain + bags                 → Operation module (TypeDefinitionNode[] + operation bodies)
4 Check     module                        → Interpretation AnalysisResult (fail-closed)
5 Consume   module + bags                 → execute | print module | host artifacts
```

| Stage | In | Out | May read bags? | Frozen |
|-------|----|-----|----------------|--------|
| **0 Parse** | `.poly` + Grammar tables | `Domain` facts | no | Grammar is media; spell closed |
| **1 Load** | `uses` ids | analyzers, maps, `IArtifactContributor` | n/a | session libraries |
| **2 Analyze** | `Domain` | concern bags on nodes; replacements | producing them | analysis pipeline |
| **3 Lower** | facts + bags | **one** operation module (generic Syntax) | **process** yes; **tree** no | shipped ⊆ Node |
| **4 Check** | module | Interpretation analysis of that module | no Domain bags | analysis required |
| **5 Consume** | module + surface bags | (a) execute with a bound directory (b) print the module (c) host files from **surface bags**, operations already in the module | artifacts only | doors map catalog; no `Main` in core |

**Two products, still:** the module (stage 3–4) and host artifacts (stage 5c). Simulate is 5a on the **same** module as 5b.

Load is listed after parse because `ForSource` peeks `uses` then loads, then parse/fold uses those tables. In code, peek+load happen before a full parse. The **logical** order is: know ids → load → parse facts → analyze.

### Stage 5 is three consumers, not three pipelines

| 5a Execute | Bind a directory + `This`; run a named operation from the **cached module** |
| 5b Project | Print the module (`CSharpGenerator` today) |
| 5c Artifacts | Persistence / HTTP / later CLI files from **surface bags**. Fail closed if a bound operation is missing from the module |

Doors do not lower. They map catalog → routes/flags. Persistence does not lower. It implements directory jobs the module already names.

---

## What runs today (the mess)

```text
parse/fold → Domain (+ DomainExpression / Effect IR)
session.Analyze → bags
                    ├─ invoke_action: LowerActionBody(LowerStageTransitions: false) every call
                    ├─ session.Emit: DomainProgramProjection → DomainToCSharpExporter
                    │                 (LowerStageTransitions: true) → CSharpGenerator
                    └─ DslCompiler: Emit + DbContextGenerator(Domain, storage bag)
                                    + MinimalApi (Domain + bags)
RuntimeAnalysisCache: Open(core catalog) — vendor maps dropped
```

| Distortion | Why it is illogical |
|------------|---------------------|
| `LowerStageTransitions` | Stage 3 is not one product |
| Re-lower on every invoke | Stage 3 is not a compile; it is a side effect of 5a |
| Projection façade over C# exporter | Stage 3 is named “language-agnostic” and implemented as C# |
| Host generators walk `Domain` | Stage 5c re-derives operations the module should already hold |
| `RuntimeAnalysisCache` | Stage 2 is not “the session you loaded” |
| `now`/`today`/`guid` → literals | Stage 3 mutates meaning before the tree exists |
| Effect IR as simulate input | Stage 5a still knows authoring IR |

Authoring IR (`DomainExpression`, `Effect`) can remain **parse output** (stage 0). It must not be **execution input** (stage 5a).

---

## Target session shape (names for what they are)

Do not invent `ICompilationPipeline`. Grow `DomainSession` (and `DomainCompilation` for parse/seed):

| Method | Stage | Today |
|--------|-------|-------|
| `ForSource` / `Open` | 0–1 | exists |
| `Analyze(domain)` | 2 | exists |
| `Lower(domain, analysis)` → module | 3 | hidden inside projection + `LowerActionBody` |
| Interpretation analyze of that module | 4 | `TryAnalyzeForEmit` only on the C# path |
| `Emit(module)` | 5b | `Emit` lowers and prints |
| Artifact contributors | 5c | `DslCompiler` + `session.Artifacts` |
| Simulate | 5a | `DomainEntityInstance` re-lowers |

Stop condition for the whole transformation: **`rg LowerStageTransitions` is gone**; simulate and `session.Emit` take the same `Lower(...)` result; invoke does not call `LowerActionBody` from Effect IR.

---

## Slices (narrow; stop and reassess)

Admit **one** slice as CURRENT at a time. Each slice has a stop. Do not combine with dict-sqlite, CLI, or EF codegen.

### P1 — One lower, one module

**Job:** Stage 3 produces one tree. Kill `LowerStageTransitions` for create / create-in / unique.

- Runtime and export both emit `this.Create` / `CreateIn` / `EnsureUnique` (or whatever the **one** job names are today).
- C# print of those jobs is allowed to look like ordinary methods. Persistence indexes stay a **5c** concern (schema), not a second create body.
- Failing test: the module used by `session.Emit` contains the same create `Invoke` shape as `LowerActionBody` for one create-in action.
- **Stop:** `rg LowerStageTransitions` empty (or the flag is a no-op deleted in the same change). Export goldens updated. No new flag.

This is the slice that makes the pipeline *true*. Do it first.

### P2 — Compile once

**Job:** Stage 3+4 run once per analyze. Stage 5a/5b consume the cache.

- `DomainSession` (or a small compile result type) holds the module + Interpretation analysis.
- `invoke_action` / `evaluate_policy` run the cached operation body. Cache key is the module (or per-operation node), not a Domain walk.
- **Stop:** create-in simulate still green; `LowerActionBody` is not on the invoke hot path.

### P3 — Projection is the lower, or the façade dies

**Job:** One owner of stage 3.

- Finish moving lower into `DomainProgramProjection` (or a successor named for what it is: `DomainModuleLowering`) **or** delete the projection and call the exporter’s lower only through that one door.
- `session.Lower` is that door. `session.Emit` prints. Runtime uses `session.Lower`.
- **Stop:** `DomainProgramProjection.ToSyntax` no longer says “delegates to DomainToCSharpExporter which will be migrated.” One call graph.

### P4 — Host artifacts do not re-lower operations

**Job:** Stage 5c binds the catalog + surface bags. Operation bodies come from the module.

- DbContext / Minimal API must not print a **second** Checkout/create implementation.
- Fail closed if `uses http` names an action that is not in the module.
- HTTP/CLI catalogs are maps of the **domain catalog**, not a second operation list.
- **Stop:** one create-in action exists only in the module; Program.cs calls it; it does not contain a copied effect walk.

Leave OpenAPI / gRPC / CLI as `?` artifacts. Do not add `uses cli` in this slice.

### P5 — One analysis door

**Job:** Stage 2 is always the session you loaded.

- `RuntimeAnalysisCache` / static `DomainModelAnalyzer.Analyze` must not reopen a core-catalog session that drops vendor maps.
- MCP, runtime, compiler share `DomainSession.Analyze`.
- **Stop:** `rg RuntimeAnalysisCache` either gone or clearly “same session,” with a test that `uses sqlite` maps are visible on the simulate path.

### P6 — Clocks are in the tree

**Job:** Stage 3 does not rewrite `now`/`today`/`guid` to host literals.

- CORE already says the VM executes `DateTime.UtcNow` / `DateOnly.FromDateTime`. Remove the preprocess lie.
- **Stop:** `rg PreprocessRuntimeKeyword` empty (or the method does not exist); a Date property assign of `Now` still works.

---

## Out of scope (this file)

- SQLite `:memory:` / EF Store as the directory (consumer 5a bind — PULL: EF codegen; parked dict-sqlite ABI is stale)
- `uses cli` / TUI / gRPC / OpenAPI (more 5c libraries after P4)
- Virtual actors, `Insert`/`Link`, deleting `DomainEntityInstance` in the same campaign
- Grammar wrap-up, mut-safety, V3 naming (THEN / PARKED elsewhere)
- A `HostSurface` framework

---

## How to tell it worked

A reader can say, without flags:

1. Analyze once.  
2. Lower once to a module.  
3. Check that module.  
4. Hand the module to simulate **or** C# print.  
5. Hand surface bags to host files that **call** the module, not rewrite it.

That is the pipeline that matches frozen core. Everything else is a consumer.

---

## Admission

When ready: one CURRENT suite for **P1 only** (task files + gate). P2–P6 wait. Do not mega-suite.
