# Experiment: Domain + DSL + Lowering Extension Platform

**Date:** 2026-07-18  
**Status:** **Superseded for execution framing** by [`docs/plans/dsl-plugin-pipeline-experiment.md`](../plans/dsl-plugin-pipeline-experiment.md) (2026-07-21 **rev 3**: P0 locked; multi-DBMS + DomainModeling seams + pack libraries). Keep this file as research memory (facets, threats, questions).  
**Home:** `docs/experiments/` (speculative design memory).  
**Pointer from plans:** [`docs/plans/domain-plugin-extension-platform.md`](../plans/domain-plugin-extension-platform.md)  

---

## Research charter

### Problem

Product DomainModeling has a **closed** DSL and constraint set (`required`, `range`, `length`, `pattern`, `unique`, …). Real software engineering needs **foreign-system metadata** and occasional **authoring extensions** (e.g. `column("order_total")` for SQL/EF mapping, OpenAPI names, PII labels) without:

- Warping core domain semantics  
- Inventing domain-specific VM opcodes  
- Teaching agents lab syntax that `apply_dsl` rejects  
- Building a plugin host before any shipping pack  

### Hypothesis

A **host-registered extension pack** model (C#) can extend:

1. **Domain IR** (facets/annotations vs validating constraints)  
2. **DSL parse/print** (new keywords with round-trip)  
3. **Analysis** (`INodeAnalyzer` contributions)  
4. **Target export / lowering-to-host** (SQL, OpenAPI, …)  

…while **execution lowering** remains CORE-faithful: only existing Syntax AST → VM.

North-star first consumer: **`column("…")` / optional `table("…")` SQL mapping pack** (annotation + exporter, no CallAction change).

### Non-goals (for this experiment forever, or until rechartered)

- MEF/open assembly marketplace as v0  
- Resurrecting product event/publish tools via messaging facets  
- Plugin-defined VM opcodes or emitter forks  
- Replacing dual authoring (DSL + micro-tools) with “everything is a plugin”  
- Active implementation while MCP dogfood / Phase 3 pull slices are the product pick  

### When to start research (gates — all preferred)

| Gate | Signal |
|------|--------|
| **G1 Consumer** | Named product or dogfood finding: “cannot express DB/API mapping in domain” |
| **G2 Stability** | Product DSL guide + parse/print + evolution green; no open honesty crisis on core constraints |
| **G3 Scope lock** | First pack is **annotation-only** (or one validating constraint with clear eval story) — not “full lowering plugins” |
| **G4 Owner** | Someone will land P1–P2 as a thin vertical, not a framework epic |

If gates fail → keep this document as design memory only.

### Research questions (answer before product code)

1. Facet vs `Constraint` hierarchy — separate type or tagged aspect?  
2. Unknown facet on `apply_dsl` — fail-loud vs opaque preserve?  
3. Extension set identity on MCP session / domain header for reproducibility?  
4. Printer canonical order for mixed constraints + facets?  
5. Does first exporter live in `DomainModeling`, a new project, or host-only?  
6. How does product `poly-dsl-agent-guide` list **enabled** packs without lying?  
7. Attribute bootstrap (`ClrTypeEntityMapping`) ↔ facet mapping for CLR-imported entities?  

### Research activities (when unblocked)

| ID | Activity | Output |
|----|----------|--------|
| **R0** | Re-read CORE + this design body; confirm seams still match code | Spike notes or ADR draft “not yet” |
| **R1** | Inventory closed constraint parse/print/evolution paths | Seam checklist (files + types) |
| **R2** | Sketch minimal `Facet` IR + one hard-coded `column` round-trip (spike branch) | Throwaway or PR-ready P1 only if G1–G4 met |
| **R3** | Paper-design SQL exporter smoke (string DDL) without shipping | Acceptance criteria for P2 |
| **R4** | Compare to dogfood report pains (if any map to mapping/metadata) | Go / no-go for execution plan |
| **R5** | If go: write **execution** plan (P1–P2 only) under `docs/plans/` and demote this to “superseded by …” | Active plan + ADR if needed |

### Success criteria for the *research* program

- [ ] Research questions 1–7 have written recommendations (even “defer”)  
- [ ] Explicit **go / no-go / wait-for-dogfood** decision recorded at bottom of this file or in an ADR  
- [ ] If go: execution plan exists with thin vertical (facet IR + `column` + round-trip + optional exporter string)  
- [ ] If no-go: reason captured; this stays experiments-only  

### Failure criteria (stop and archive tone)

- Building `IDomainExtension` host with zero packs  
- Teaching agents `column` in product guide before IR exists  
- “Security” facets without enforcement story presented as product guarantees  

---

## Design body (exploration)

The following sections are **design memory** for future researchers. They are not a task checklist for current agents.

### 1. Motivating example

```poly
Order: entity {
  Total: Number range(0, ) column("order_total") required
  Status: Text column("status_code")
}
```

`column("order_total")` is **target-system metadata**, not a validation constraint like `range` / `required`.

| Kind | Role | Examples | Affects runtime VM? |
|------|------|----------|---------------------|
| **Semantic constraint** | Restricts domain values / identity | `required`, `unique`, `range`, `pattern` | Often yes |
| **Annotation / facet** | Labels for another system | `column`, `table`, `jsonName`, `pii`, `metric` | Usually **no** |
| **Lowering contribution** | Changes domain → AST / host code | custom effect, custom expr op | Yes if executable nodes |

Today: closed `Constraint` records + hard-coded DSL for `range` / `length` / `pattern` / `required` / `unique`. No open registration of syntax → IR → printer → analysis → export.

### 2. What “plugin” should mean (layers)

```text
L0  Domain IR vocabulary (Constraint / Facet / Effect / Expr types)
L1  Authoring (DSL parse/print, evolution, MCP)
L2  Interpretation & targets (analyzers, Syntax lowering, SQL/OpenAPI exporters)
      ▲
      └── Package / discovery (host registration first; assembly load later)
```

| Approach | Fit | Risk |
|----------|-----|------|
| First-party extension packs (`UseSqlMapping()`) | Best first slice | Not third-party yet |
| `IDomainExtension` + assembly catalog | Real plugins | Versioning, trust, determinism |
| Source generators | Strong typing | Weak third-party story |
| Data-only packs (JSON facet schemas) | No custom IL | Weak for true lowering |

**Principle fit:** start with **explicit host registration** of packs; open loading only after a second pack + trust story. Working code before plugin frameworks (AGENTS §6).

### 3. Pipeline seams

```text
.poly / MCP / API
  → PolyDslParser / DomainChange / DomainEvolution
  → Domain + Constraint / Facet / Effect / Expression
  → DomainModelAnalyzer
  → DomainExpressionLoweringPass / EffectLoweringPass → Syntax AST
  → Interpretation + DirectVmAbiEmitter → VM
  → (future) Target exporters
```

| Seam | Today | Extension hook (sketch) |
|------|--------|-------------------------|
| DSL parse/print | Closed switches | Registered annotation/constraint syntax |
| Evolution | `AddConstraintToPropertyChange` | Generic payload or facet-specific changes |
| Domain IR | `Constraint` only | Separate **Facet** (recommended) |
| Analysis | Fixed pipeline | Contributed `INodeAnalyzer` + dependencies |
| Lowering (exec) | Closed | Lower to **existing** Syntax only (CORE) |
| Lowering (target) | Not productized | `IDomainTargetExporter` |
| MCP / guide | Closed product surface | Enabled-pack honesty |

**CORE:** no domain-specific VM opcodes; no parallel type registry or emitter fork.

### 4. Constraints vs annotations

If `column` is a `Constraint`, agents and analyzers will treat it like validation.

**Sketch:**

```text
DomainObject
  Constraint   // validating
  Facet        // foreign-system metadata
```

`column` pack: parse/print, store on property, optional duplicate-column analysis, SQL/EF export — **not** policy/CallAction semantics.

### 5. Broader engineering concerns

See full tables in the original exploration: persistence mapping, API contracts, PII/compliance, observability, messaging (without product events), i18n, multi-store, plus platform hard problems:

Determinism · versioning · unknown-facet policy · name collisions · printer order · equality · serialization · trust · dependency direction · tests · agent honesty · analyzer performance · Validation composition · CLR attribute bootstrap · evolution diffs · per-session extension sets.

### 6. Illustrative C# vocabulary (not API commitment)

```csharp
public interface IDomainExtension {
    string Id { get; }      // "poly.sql-mapping"
    Version Version { get; }
    void Contribute(DomainExtensionBuilder builder);
}
// Builder: Annotation / ValidatingConstraint / Analyzer / TargetExporter
// Deliberately no RegisterVmOpcode()
```

Host: `DomainExtensionSet.Create().Add(new SqlMappingExtension()).Build()`; MCP session records extension set hash.

### 7. Lowering rules

| Intent | Allowed | Forbidden |
|--------|---------|-----------|
| SQL/API metadata | Facets + exporter | Custom VM ops |
| Runtime validation | Policy / Validation / existing expr lower | Side effects in analyzers |
| New effects | Existing Syntax patterns or host integration | Domain opcodes |

### 8. Threats

Plugin soup · silent facet drop · guide drift · security theater · analyzer order wars · version skew · MCP tool explosion · second-system plugin host.

### 9. Possible future execution phases (only after gates + go)

| Phase | Deliverable |
|-------|-------------|
| P0 | Research (this charter + R0–R5) |
| P1 | Annotation IR + one facet round-trip |
| P2 | In-tree SQL mapping pack + exporter smoke |
| P3 | Registry-driven parse/print |
| P4 | Analyzer contributions |
| P5 | Optional assembly discovery |
| P6 | MCP enabled-extensions honesty |

Do **not** start at P5. Prefer host-owned discovery over core `Poly` loaders.

### 10. Relationship to current product

| Track | Interaction |
|-------|-------------|
| MCP dogfood | May supply G1 evidence |
| Product DSL guide | Core-only until packs exist |
| Runtime MCP / CallAction | Orthogonal |
| Event tools | Still never |

### 11. Open design questions

Listed under Research questions above (same set).

---

## Decision log (fill when research runs)

| Date | Decision | Notes |
|------|----------|-------|
| 2026-07-18 | Parked as experiment | Design captured; no execution; wait for consumer + gates |

---

## Bottom line

**Research later, implement only with a named consumer.**  
A Poly extension platform is primarily **facets + authoring + export**, with execution lowering only via existing Syntax. `column("…")` is the intended first pack. This file is experimental memory until gates open and a thin execution plan replaces P1–P2.
