<!-- Research note, 2026-10-12. Author: agent, at Scot's request ("what else does Poly need to be the best ontology-of-ontologies tool"). External sources are linked; repo claims cite repo-relative paths. Line numbers are as of master 86148036; re-check at slice start. Not a queue: admission is [`../board.md`](../board.md). -->

# Poly as an "ontology of ontologies": gap map and roadmap (2026-10-12)

Scope: what Poly has, what a best-in-class *meta* tool for building ontological tools needs, and the gap between them. "Ontology of ontologies" = a tool that helps agents and humans build the tools that model domains: author a domain (ontology), validate it fail-closed, execute it, and emit the artifacts other toolchains consume.

Method: (a) inventory Poly's current capability from the repo, (b) survey the closest external tools and the 2026 agent-era norms, (c) list gaps from trivial to strategic with an evidence link each.

---

## 1. Where Poly stands (the moat)

Poly's differentiator is that a domain is a **library of legal operations**, not just a schema. The pipeline is Load → one analyze (fail-closed) → `session.Lower` → artifact set, and the interpreter sim and the C# print share one operation AST (`docs/CORE.md`, `docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`). That buys things no schema language has:

- **Executable semantics**: stage machines, actions, policies, stage subscriptions (`when X as y { … }`), effects (`assign`/`create`/`create in`/`transition`/`invoke`/`for-invoke`) — a domain runs.
- **Fail-closed analysis** with coded diagnostics (DM*, VM) before anything ships.
- **Sim/print parity** enforced by tests (`Poly.Tests/DomainModeling/Lowering/ParityTests.cs`, golden emit tests) — the same body drives the VM and the printed C#.
- **Agent-first authoring** over MCP: `get_dsl_guide`, `apply_dsl`, `get_domain_analysis`, `get_domain_suggestions`, `export_dsl`, `export_domain_to_csharp`, plus runtime tools (`create_instance`, `invoke_action`, `evaluate_policy`, `link_instances`).

None of the external tools below combine runtime meaning + code-gen parity + an agent loop. That is the position to defend.

---

## 2. The landscape

| Tool | What it is | Lesson for Poly |
|---|---|---|
| [LinkML](https://linkml.io) | Single-source schema language with **30+ generators** (JSON Schema, GraphQL, ProtoBuf, OWL, ShEx, SHACL, RDF, JSON-LD, SPARQL, SQL DDL, Pydantic/TS/Java/Rust, docs, ER diagrams, Excel, TypeDB). "Embrace and reuse existing frameworks." | The meta-tool benchmark. Flagship extras: **annotated enums / value sets** (members mapped to ontology IRIs) and **imports** between models |
| [TypeSpec](https://typespec.io) | Microsoft IDL for API contracts; stable OpenAPI 3 + JSON Schema emitters, Protobuf preview, linter, **web playground**, IDE support | Single source → specs/docs/clients; a playground drives adoption |
| [TypeDB](https://typedb.com) | "Executable ontology" database: type **hierarchies**, N-ary relations with role cardinality, inference rules, **type-checked queries that fail loud** | Their whole pitch ("tells you when you've got it wrong") is Poly's fail-closed pitch, but as a DB |
| [OntoGPT](https://github.com/monarch-initiative/ontogpt) + Neo4j 2026 | LLM ontology extraction over LinkML templates; ships **Agent Skills** for agents to drive it | The ecosystem now expects ontology tools to ship Agent Skills; the winning pattern is *"AI proposes, deterministic validation disposes"* |

Trend to ride: ontologies are becoming the agent memory layer, and the value has moved from *building* the model to *proving* it (deterministic validation). Poly is already on the right side of that; it lacks the emitters and the packaging.

---

## 3. Gap map

Each item: what, why, evidence/anchors, rough size (S/M/L).

### Tier 1 — trivial, high leverage ("don't miss anything trivial")

1. **JSON Schema emitter** (S). Agents and tooling ask for it first. A thin artifact over the existing lowering; today there is no JSON Schema output anywhere in `Poly/`/`src/`.
2. **JSON-LD context + RDFS export** (S). Cheapest door into the semantic-web world; gives every entity/property a resolvable term.
3. **Per-domain docs + data dictionary** (S). `Poly/Interpretation/Mermaid/MermaidAstGenerator.cs` already emits Mermaid; add a searchable docs artifact and Excel/CSV export (LinkML ships both).
4. **Annotated enums / value sets** (S). Enums exist; add optional `iri`/`term` metadata per member so a value set can map to schema.org/FHIR/OBO (LinkML's flagship feature).
5. **Term annotations** (S). Optional `iri`, `label`, `description`, `deprecated`, `version` on entities/properties. Ontologies without IRIs/terms are not ontologies, and governance needs provenance.
6. **Shipped Poly Agent Skill** (S). A `SKILL.md` teaching the golden workflow (`get_dsl_guide` → `.poly` → `apply_dsl` → analyze → export). OntoGPT and Neo4j both ship skills; the repo already has the skill infrastructure in use.
7. **Domain versioning + semantic diff** (S–M). A `version` on the domain header plus a breaking-change check; the probe/golden/diff machinery already exists to build on.

### Tier 2 — interop emitters (LinkML parity)

8. **SHACL shapes export** (M). Poly's constraints and policies map naturally to shapes; SHACL is what validation-heavy teams actually consume (LinkML's own guidance: OWL for vocabularies, SHACL for validation).
9. **GraphQL + OpenAPI 3 emitters** (M). `docs/domain-modeling/first-principles.md:324,366` already frames OpenAPI/gRPC as host libraries, and [`docs/plans/parked/ef-and-api-codegen.md`](../../parked/ef-and-api-codegen.md) is the parked plan — un-park it as the emitter workstream.
10. **ProtoBuf/gRPC emitter** (M); **SQL DDL artifact** (S) for non-.NET consumers (storage packs exist, but a pure DDL artifact does not).
11. **Importers**: JSON Schema → `.poly`, LinkML → `.poly`, partial OWL → `.poly` (M–L). Round-trip today is only Poly's own DSL; the meta-tool claim needs the inbound direction.
12. **OWL export** (M) — class hierarchy + object properties. Only worthwhile once inheritance (item 13) exists.

### Tier 3 — expressiveness real ontologies need (strategic; language change)

13. **Entity inheritance / abstract entities / mixins** (L). TypeDB's core and LinkML's is-a + mixins. The single biggest "ontology" credibility gap; touches analyzer, lowering, and the sim together.
14. **Many-to-many (N:N) / reified relationship entities** (L). The DSL is deliberately N1-only today (`Poly.Mcp/Docs/poly-dsl-guide.md` §4 "Navigation Properties (N1 Relationships Only)"), and the repo already flags the risk: "Ownership, many-to-many … need careful analyzer rules" (`docs/plans/archive/experiments/domain-modeling-dsl-tour-feedback.md:127`).
15. **Cardinality annotations** on navigations (0..1, 1..\*, required) (M). Currently implied by the navigation kind.

### Tier 4 — ecosystem and governance moat

16. **Domain package registry** (M). Domain libraries exist (`docs/decisions/2026-08-14-domain-libraries.md`; `uses` ids), but there is no distribution story: NuGet packaging + a catalog index + stable w3id-style IDs.
17. **Starter ontology pack** (M). Publish dogfood domains: a schema.org subset, retail/inventory, PII/FHIR-lite, temporal events. Content is the cheapest credibility.
18. **Governance artifacts** (M). Productize "the ontology engineer as governor": provenance report (term → origin), constraint/shape report, parity/canary gate. The internals already exist (`VmAnalyzerReport`, golden parity, fleet-eval probes).
19. **Playground** (L). A web REPL for `.poly` (TypeSpec and LinkML both have one). Biggest single adoption lever.
20. **`.poly` LSP** (M) for human editing; **MCP hardening** (M) — session isolation for shared servers (flagged 🟠 in `docs/probes/findings/fleet-eval/`), plus resources/notifications so an agent can observe domain changes. MCP mut-safety is already on the board's THEN line.

---

## 4. Recommended wedge and sequencing

Positioning: **"LinkML's polyglot emitters + TypeDB's executable semantics + Poly's fail-closed agent loop."**

Order:

1. **Agent story first (Tier 1).** Ship the Poly Agent Skill, JSON Schema + JSON-LD/RDFS emitters, term annotations/IRIs, annotated enums, and domain docs. Small, visible, and it makes the existing MCP loop legible to other agents.
2. **Un-park codegen (Tier 2).** SHACL, then OpenAPI/GraphQL, then ProtoBuf/SQL. This is the "30+ generators" bar and reuses the parked EF/API plan.
3. **Inbound interop (item 11)** — JSON Schema/LinkML import — the day a second tool wants to round-trip with Poly.
4. **Admit expressiveness as its own slice chain (Tier 3).** Inheritance first, then N:N. These are language changes and deserve the same slice discipline as C8, not a rider on an emitter PR.
5. **Ecosystem (Tier 4)** once there is something to distribute: registry, starter pack, playground.

What *not* to chase yet: full open-world OWL reasoning (LinkML's stance — OWL for vocabularies, SHACL for validation — is the pragmatic line), and entity resolution / KG extraction (OntoGPT's lane; Poly is the schema+validation side of the "AI proposes, deterministic validation disposes" split).

---

## 5. Open decisions (need Scot)

- **Audience order**: agents-first (skills/emitters), data-engineering interop (JSON Schema/SQL/OpenAPI), or the knowledge-graph/OBO crowd (OWL/SHACL/RDF)?
- **OWL appetite**: SHACL + JSON-LD now and OWL only for vocabularies, or invest in open-world OWL?
- **Inheritance + N:N**: sketch a C9-style slice chain now, or keep this note confined to emitters/agent UX until C10 lands?
- **Registry**: NuGet-only, or a hosted catalog index with stable term IDs?

---

## 6. Verified inventory (evidence)

Present today:

| Capability | Anchor |
|---|---|
| Executable sim + fail-closed analyze + Lower | `docs/CORE.md`, `docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md` |
| Sim/print parity tests | `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` |
| C# emit | `Poly/DomainModeling/Lowering/DomainToCSharpExporter*.cs` |
| Mermaid visualization | `Poly/Interpretation/Mermaid/MermaidAstGenerator.cs` |
| MCP authoring/runtime tools | `Poly.Mcp/Tools/*.cs` |
| Domain libraries + `uses` | `docs/decisions/2026-08-14-domain-libraries.md` |
| Storage packs (SQLite/SQL Server/MySQL) | `src/Poly.Packs.*` |
| Suggestions + golden/probe gates | MCP `get_domain_suggestions`; `docs/probes/**` |

Absent (verified 2026-10-12):

| Missing | Evidence |
|---|---|
| JSON Schema / OpenAPI / GraphQL / Protobuf emitters | no matches in `Poly/`, `src/`, `Poly.Mcp/` |
| OWL / SHACL / RDF / JSON-LD / SPARQL | no matches in `Poly/`, `src/`, `Poly.Mcp/` |
| Entity inheritance / mixins / abstract entities | DSL has no such construct |
| Many-to-many relationships | `Poly.Mcp/Docs/poly-dsl-guide.md` §4 (N1 only) |
| Enum member → external term mapping | enums exist without term metadata |
| EF Core / API codegen | parked: [`docs/plans/parked/ef-and-api-codegen.md`](../../parked/ef-and-api-codegen.md) |
| Domain package registry / playground / `.poly` LSP | none |

---

## Sources

- LinkML — <https://linkml.io> · generators and philosophy (<https://linkml.io/linkml/faq/why-linkml.html>)
- TypeSpec — <https://typespec.io> · emitters (<https://typespec.io/docs/emitters/json-schema/reference/emitter>)
- TypeDB — <https://typedb.com> · type hierarchies, N-ary relations, fail-loud queries
- OntoGPT — <https://github.com/monarch-initiative/ontogpt> · Agent Skills
- Agent-era ontology construction (2026): "AI proposes, deterministic validation disposes"; MCP ontology-to-tools compilation; ontologies as agent memory (<https://arxiv.org/html/2602.03439v1>)
