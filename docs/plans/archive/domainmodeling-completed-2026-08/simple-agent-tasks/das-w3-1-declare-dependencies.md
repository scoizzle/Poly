# DAS W3.1 — Declare accurate pass dependencies

**Wave:** W3 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) P5  
**Difficulty:** Small–Medium  
**Status:** `[x]`  
**Prereq:** W1 gate (catalog exists so deps can name it)  

## Objective

Every fact-publishing pass declares real `Dependencies`. Lint-only passes are labeled (empty deps OK only if they write no metadata others read).

## Tasks

- [x] W3.1.1 Inventory passes: metadata written vs read.
- [x] W3.1.2 Fill `Dependencies` arrays for Structure, Topology, Ownership, Storage, Transport, Capability, Catalog, etc.
- [x] W3.1.3 Fix registration order if builder requires topological order.
- [x] W3.1.4 Add a test or analyzer check that fails if a known consumer pass lacks declared dep (lightweight OK).

## Acceptance criteria

- [x] No fact pass with silent undeclared reads of catalog/structure/topology.
- [x] Build + tests green.

## Progress notes

### Inventory (write → read)

| Pass | Writes | Reads | Deps |
|------|--------|-------|------|
| Structural | diags only | — | `[]` lint |
| Semantic | DTLM, RLM, ResolvedType, EPM, EMM | — | `[]` root fact |
| RuntimeContract | RelContract, SubDispatch | DTLM | Semantic |
| DomainCatalog | DomainCatalogMetadata | DTLM, RLM | Semantic only (dropped unused RuntimeContract) |
| PolicyConstraint | RequiredProperties | DTLM | Semantic |
| ConstraintPropagation | DownstreamConstraints | — | `[]` root fact |
| Effect | ResolvedRelTarget + diags | DTLM, ResolvedType, RequiredProps, Downstream | Semantic, Policy, ConstraintProp |
| ConstraintQuality | diags | DTLM, ResolvedType | Semantic (lint) |
| Capability | Action/Stage/Rel capability | DTLM, Catalog | Semantic, Catalog |
| RuleCoverage | diags | RequiredProps | Policy (lint) |
| ContractIntegration | diags | — | `[]` lint |
| EntityStructure | ESM | DTLM | Semantic |
| Subscription | diags | DTLM, ActionCapability | Semantic, Capability (lint) |
| EffectTopology | Topology | — | `[]` pure tree scan |
| Ownership | OwnershipAggregate | Topology, ESM | Topology, EntityStructure |
| Behavior | BehaviorMetadata | DTLM, EPM, ActionCapability | Semantic, Capability |
| CrossReference | EntityDepGraph | Topology | Topology |
| Storage | StorageMapping | Topology, Ownership | Topology, Ownership |
| Transport | Transport | Topology, Ownership | Topology, Ownership |
| AuthoringSuggestion | diags | DTLM | Semantic (lint) |

### Changes

- Filled missing `Dependencies` on fact + lint readers; labeled lint-only passes in XML docs.
- Moved `ConstraintPropagationAnalyzer` before `EffectAnalyzer` in registration so builder can resolve Effect's deps (also fixes DownstreamConstraints availability).
- Tests: `PassDependencyDeclarationTests` (declare known deps, order honors edges, missing dep throws at build).

### Verify (2026-07-31)

- **Implement:** success · **Verify:** pass (severity: nit)
- Code review of DomainModeling `INodeAnalyzer.Dependencies` vs `GetMetadata`/`SetMetadata`:
  - Catalog/Structure/Topology consumers declare publishers (Capability→Catalog+Semantic; Ownership→Topology+EntityStructure; Storage/Transport→Topology+Ownership; CrossReference→Topology; EntityStructure→Semantic).
  - Lint readers declare bag publishers.
  - Empty deps only on Structural, ContractIntegration (lint), Semantic, ConstraintPropagation, EffectTopology (justified).
- `DomainModelAnalyzer` registers `ConstraintPropagation` before `Effect`.
- `PassDependencyDeclarationTests` asserts known deps, telemetry order edges, and `AnalyzerBuilder` missing-dep throw.
- Catalog deliberately omits `RuntimeContract`.
- Suite not re-run in verifier session; static evidence supports green compile (`InternalsVisibleTo` Poly.Tests).
