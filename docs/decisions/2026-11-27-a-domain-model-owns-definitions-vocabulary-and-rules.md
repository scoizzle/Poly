# A domain model owns its definitions, vocabulary and rules

Status: **Draft for owner review, 2026-11-27.** Nothing here is decided. Base: `36f62a28e` (`stream/session-2026-11-21`).
Modeled on [A type system owns its definitions, vocabulary and rules](2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md)
(#998). Open questions for the owner are in §9; the rows in §7 do not start until they are answered.

## Why

The type system fix had one root cause: a question had no owner, so it went on the nearest data type and every pass
reached for it there. The domain side shows the same shape. Questions about a `Domain` ("which entity has this name?",
"is this property required?", "what is this entity's stage enum called?", "is this policy name a negation?") have no
owning object. Each pass answers them with its own scan, its own string, or a static helper on an unrelated class.

All counts below were taken at `36f62a28e` with the commands shown. They are mine, not the audit's, and where they
differ from #952 or #953 (taken at `773edfe5`) the newer figure is the one to use.

### 1. Lookups have a door, and almost nobody uses it

`Analysis/DomainSemanticLookupExtensions.cs` has 21 statics (16 public, 5 internal) over six lookup bags
(`DomainCatalogMetadata`, `DomainTypeLookupMetadata`, `RelationshipLookupMetadata`, `MutationTargetIndexMetadata`,
`ActionResolutionMetadata`, `EntityStructureMetadata`).

| Fact | Command | Count |
|---|---|---|
| Plain `OfType<Entity>` occurrences, production | `grep -rn 'OfType<Entity>' --include=*.cs Poly src Poly.Mcp \| grep -v /obj/` | 110, in 49 files |
| Of those, name lookups on one line (`OfType<Entity>().FirstOrDefault(…)` and the `First`/`Single` forms) | same, regex `OfType<Entity>\(\)\s*\.(FirstOrDefault\|First\|SingleOrDefault\|Single)\(` | 11 |
| Name lookups continued on the next line | `grep -A1 'OfType<Entity>()\s*$'`, next line starts `.FirstOrDefault`/`.First`/`.Single`/`.Where`/`.Any` | 21 |
| Production callers of the catalog door `TryGetEntity` | `grep -rn '\.TryGetEntity(' --include=*.cs Poly src Poly.Mcp` | 1 |
| Private copies of "entity by name" | `grep -rnE '(private\|internal\|public) (static )?Entity\? \w+\((string\|Domain\|AnalysisContext)'` | 5: `EffectLoweringPass.Create.cs:236`, `DomainExpressionLoweringPass.cs:148`, `ExpressionTypeInference.cs:265`, `EffectAnalyzer.cs:734`, `DomainMutationContext.cs:309` |
| Dead doors | `GetOutboundRelationships`, `GetInboundRelationships`, `TryGetEnumType`: no callers anywhere (production or tests) | 3 |

So at least 32 of the 110 `OfType<Entity>` sites are name lookups that bypass the fail-closed door (#953). The other
~78 are enumerations or shapes the regex does not classify; they are legitimate until a row shows otherwise. The
"106" in the research brief came from a broader regex over fewer directories; use 110 / 32.

The stage-copy fallthrough rule (an empty stage action defers to the entity action) is written twice:
`DomainAnalysis.FindAction` (`Analysis/DomainAnalysis.cs:31`) and `TryResolveAction`
(`DomainSemanticLookupExtensions.cs`, #953).

### 2. A rule lives on an analyzer, and 20 call sites reach in for it

`EntityStructureAnalyzer.IsRequired(Property)` is one line (`!property.Constraints.Any(c => c is OptionalConstraint)`,
`Analysis/EntityStructureAnalyzer.cs:132`) and is called from outside the class at exactly 20 sites in 14 files
(`grep -rn 'EntityStructureAnalyzer\.IsRequired' --include=*.cs Poly src Poly.Mcp`):

- Analysis: `GuardClosedProperties.cs:151,280,284`; `ExpressionTypeAnalyzer.cs:104,157`; `ExpressionTypeAnalyzer.Arms.Temporal.cs:53`;
  `ExpressionTypeInference.cs:144`; `EffectAnalyzer.cs:183,184`; `EffectAnalyzer.Assign.cs:100`; `EffectInvariantAnalyzer.cs:116`;
  `ProvableGuards.cs:329,355`; `TextLengths.cs:60`; `StructuralDomainAnalyzer.cs:164`; `BehaviorMetadata.cs:62`.
- Lowering: `EffectLoweringPass.Assign.cs:59,357`; `DomainExpressionLoweringPass.cs:153`.
- Meaning: `DomainTypeModel.cs:32`.

The same class carries 8 more static helpers other passes call (external call counts from the same grep):
`ComputeEntryAssignedPropertyNames` 10, `IsImplicitStage` 6, `ComputeRequiredPropertyNames` 4, `SlotKindFor` 2 (two
overloads), `TransitionsTo` 1, `StageEnumTypeNameFor` 1, `ComputeAssignOnlyPropertyNames` 1.

`docs/CORE.md:83` (the DomainModeling row) says "Lowering reads published facts for requiredness … it never recomputes them". Three
of the 20 sites are in Lowering and call the predicate directly. The predicate reads declared constraints, not the
structure bag, so this is arguably not recomputation; the doc and the code still disagree, and §3 resolves it.

### 3. Two naming conventions are string protocols (#952)

| Convention | Today | Command |
|---|---|---|
| Negation marker: a `require not P` becomes a policy named `not_P` | 15 lines contain the literal `"not_"`; only 8 references use `PolicyGuardFacts.NegatedPrefix` (`Analysis/PolicyGuardFacts.cs:22`) | `grep -rn '"not_' --include=*.cs Poly src Poly.Mcp \| grep -v /obj/` |
| Stage enum name `{Entity}Stage` | 14 lines build it by string; `StageEnumTypeNameFor` (`EntityStructureAnalyzer.cs:208`) exists and is referenced 3 times | `grep -rnE '\}Stage"\|\+ "Stage"' --include=*.cs Poly src Poly.Mcp` |

(#952 counted 12 and 13 at `773edfe5`; some sites have since moved to the helpers, new ones have appeared.) Related
open bugs from the same cause: #890 (`{Other}Stage` shadowing) and #669.

### 4. The pattern, as the type-system doc states it

> With no type-system object to own that question, it went on the definition, and it has stayed there.

Here the question went to a static helper on whichever analyzer wrote it first, and the answer was copied. Churn is
the symptom: `Analysis/` has 253 non-merge commits since 2026-10-15 (`git log --no-merges --since=2026-10-15 -- Poly/DomainModeling/Analysis/`),
and `EntityStructureAnalyzer.cs` grew from 155 to 368 lines in the same window (21 commits, `git log --follow`).
This checkout's history begins 2026-10-15, so these are six-week figures.

## Decision

Introduce `IDomainModel` as the object that owns:

- the definitions of one analysed domain and how they are found;
- the domain's vocabulary (the naming conventions);
- its rules (requiredness, create slots, stage shape);
- the questions its consumers need answered, as registered rules.

`Domain` and the `Ontology` records keep only what they declare. No `bool`, `int` or enum classification is added to
an Ontology type, and none to `IDomainModel` itself.

## 1. The interface: 12 members

```csharp
namespace Poly.DomainModeling.Analysis;

public interface IDomainModel {
    Domain Domain { get; }                                          //  1

    // Definitions: one door per kind, fail closed (#953)
    Entity? FindEntity(string name);                                //  2
    DomainType? FindType(string name);                              //  3  entity, value or enum
    Stage? FindStage(Entity entity, string name);                   //  4
    Action? FindAction(Entity entity, string name, string? stage);  //  5  the stage-copy rule, written once
    Relationship? FindRelationship(Entity source, string name);     //  6
    IReadOnlyList<Relationship> RelationshipsFrom(Entity source);   //  7  outbound
    IReadOnlyList<Relationship> RelationshipsTo(Entity target);     //  8  inbound

    // Vocabulary (#952)
    string StageEnumName(Entity entity);                            //  9  "{Entity}Stage", the one place
    string NegatedPolicyName(string policyName);                    // 10  "not_P"
    string? NegatedPolicyBase(string policyName);                   // 11  "P" for "not_P", else null

    // Rules
    TRule? Rule<TRule>() where TRule : class, IDomainModelRule;     // 12
}

public interface IDomainModelRule { }   // empty base every rule extends; kept free of classification
```

12 members, on purpose the same size as `ITypeSystem`. The guards in §6 pin that number from the row that adds it.

### What a member is not

- Nothing takes one `Entity`, `Property` or `Stage` and returns `bool`, `int` or an enum. That is the type-system
  doc's "classifies nothing" guard, and `IsRequired` would break it as a member, so requiredness is a rule (§3).
- The model holds no mutable state and no cache. §4.

## 2. What moves off which types

| Today | After |
|---|---|
| `DomainSemanticLookupExtensions`: `TryGetEntity` ×2, `TryGetStage`, `TryResolveAction` ×2, `TryGetRelationship` ×2, `GetRelationships`, `GetAllRelationships`, `GetOutboundRelationships`, `GetInboundRelationships`, `TryGetEnumType` ×2 | `FindEntity`/`FindType`/`FindStage`/`FindAction`/`FindRelationship`/`RelationshipsFrom`/`RelationshipsTo`. The three dead doors are deleted, not moved. |
| `GetEffectivePolicies`, `GetEffectiveActions`, `GetStructure`, `GetCatalog`, `GetActionResolution`, `GetMutationIndex`, `GetTypeLookup`, `GetRelationshipLookup` | `GetStructure`, `GetEffective*` stay as extension views over the model. The five lookup getters become internal to the model's backing. |
| The 5 private "entity by name" copies and `StageEntryFacts.FindStage` | Deleted. They call `FindEntity` / `FindStage`. |
| `DomainAnalysis.FindAction` and `TryResolveAction`'s copy of the stage-copy rule | One `FindAction`. |
| `EntityStructureAnalyzer.IsRequired`, `ComputeRequiredPropertyNames`, `ComputeEntryAssignedPropertyNames`, `ComputeAssignOnlyPropertyNames` | `IRequirednessRule : IDomainModelRule`. |
| `EntityStructureAnalyzer.IsImplicitStage`, `TransitionsTo` | `IStageShapeRule : IDomainModelRule`. |
| `EntityStructureAnalyzer.SlotKindFor` ×2 | `ICreateSlotRule : IDomainModelRule`. |
| `EntityStructureAnalyzer.StageEnumTypeNameFor` and the 14 `{Entity}Stage` string builders | `StageEnumName`. |
| `PolicyGuardFacts.NegatedPrefix` and the 15 `"not_"` sites | `NegatedPolicyName` / `NegatedPolicyBase`. `NegatedPrefix` becomes private to the model. |

`EntityStructureAnalyzer` keeps publishing `EntityStructureMetadata`; it stops being a library of static answers.

## 3. `Rule<T>()` and the base rule

Same shape as `ITypeSystemRule`:

- A rule is an interface that extends `IDomainModelRule`, declared by its owner, registered when a session builds, and
  asked through `model.Rule<T>()`. `Rule<T>` and registration accept nothing else.
- The core library registers `IRequirednessRule`, `IStageShapeRule` and `ICreateSlotRule`. A pack that needs another
  answer (say, a key that is required even when marked `optional`) registers its own rule rather than patching a
  switch in an analyzer.
- For a core rule that must exist, an unregistered answer is a fail-closed error, not "not applicable" (the type
  system treats null as "no rule"; here a domain with no requiredness rule has no meaning). Q6.
- Call sites keep reading naturally through extension views, which are not members: `model.IsRequired(property)`
  calls `Rule<IRequirednessRule>()`. The "classifies nothing" guard applies to the interface's members only, as it
  does for `ITypeDefinition` and `TypeDefinitionExtensions`.

This resolves the CORE.md line above: Lowering asks the model for requiredness; nothing recomputes it.

## 4. The CORE constraint: a view over `AnalysisResult` bags, never a second store

This is the constraint that decides the design. `docs/CORE.md:31`: "Analysis owns facts and rewrite | Bags on nodes;
`SetNodeReplacement`; semantic consume needs `AnalysisResult`". `AGENTS.md`'s frozen-core table: "Analysis: bags + node
replacement | Side tables; semantic consume without `AnalysisResult`".

1. The model is constructed from an `AnalysisResult` and a `Domain`: `analysis.GetDomainModel(domain)`.
2. Every field is a readonly reference to the result, the domain, or a bag the result already holds. The model
   never memoises, never owns a dictionary, and never caches a derived fact. A derived fact that is worth keeping
   is published as a bag by a pass, as `EntityStructureMetadata` already is, and the model reads it.
3. A missing bag throws, as `LoweringContext` does today ("a missing structure bag throws"). A model over an
   `AnalysisResult` that never ran the catalog pass is an error, not an empty answer.
4. The model is not an `IAnalysisMetadata`, is not stamped on a node, and is not serialized.
5. Constructing a model twice for one result yields observably equal answers. That is the proof it holds no state.

Guard (§6): a reflection test that every instance field of the model's implementation is `readonly`, of a type that
is `Domain`, `AnalysisResult`, or an `IAnalysisMetadata`, and that it declares no `Dictionary`/`List`/`HashSet`
field.

Conflicts with other principles, stated plainly:

- **"A second IR" / twin trees (hard nos).** The model is not an IR: it holds no nodes of its own and lowers nothing.
- **"Seam when multiplicity is known" (tenet 6).** Satisfied by multiplicity of use (110 / 32 / 20 / 15 / 14 above),
  not by a second implementation. If a reviewer reads that as an imaginary second use, the rules half of this
  proposal (§3) is the part to cut; the lookup and vocabulary halves stand on counts alone.
- **"Guardrails only with real consumers" (tenet 7).** The ratchets in §6 each pin a count that is non-zero today.

## 5. Isolation: who depends on it

Producers: `DomainCatalogPass`, `EntityStructureAnalyzer` (publishing the bag), the core library's rule registration.
Consumers, by the counts in §1 and §2: the semantic passes in `Poly/DomainModeling/Analysis`, `Lowering`, `Meaning`,
`Queries` (`ElementPathResolver.cs:169`), `Evolution` (see Q3), `Poly.Mcp` (`InspectService.cs:229,273`) and
`src/Poly.Packs.Product`.

It does not touch expressions, effects, or `Poly/Interpretation`. It does overlap files with the in-flight
expression-kind work (§8).

Public surface: 16 of the 21 lookup statics are `public`. Callers outside `Poly/` (`grep -rn '\.<name>(' --include=*.cs
src Poly.Mcp Poly.Tests`): `GetStructure` 6 in `src`/`Poly.Mcp` and 18 in tests, `GetAllRelationships` 10 and 15,
`TryResolveAction` 1 and 6, `GetEffectiveActions` 1 and 4, `GetEffectivePolicies` 1 and 8, `TryGetRelationship` 0 and 6;
the rest have test callers only or none. Removing them is a public
contract change, which AGENTS.md sends to a fresh-context review from another model family before merge (D6, Risks).

## 6. Guards

Each is a test, red first. Counts are pinned at today's value and only shrink.

- **`DomainModel_IsExactlyTheDecidedMembers`:** the interface has §1's 12 members, from the row that adds it.
- **`DomainModel_ClassifiesNothing`:** no member takes exactly one `Entity`, `Property` or `Stage` and returns
  `bool`, `int` or an enum.
- **`DomainModelRules_AreConstrainedToTheBase`:** `Rule<T>` and registration constrain `T` to `IDomainModelRule`.
- **`DomainModelRule_ClassifiesNothingOnTheBase`:** the base declares no members.
- **`DomainModel_HoldsNoState`:** §4's reflection test.
- **`IsRequired_ExternalReferences`:** ratchet, 20 → 0 (D2).
- **`EntityNameLookups_BypassTheDoor`:** ratchet over a regex fixed in the test (the two name-lookup shapes in §1);
  pinned at what that regex measures on the day D0 lands (32 with the commands above), → 0 (D3).
- **`NamingConventions_LiveInOneHelper`:** no literal `"not_"` and no `}Stage"` outside the model (#952's "done
  when"). Pinned at 15 and 14, → 0 (D4).

## 7. Migration rows

Each row starts with a failing test, run red and recorded; behaviour-preserving rows lower a ratchet first. Each ends
with the full suite green and Benchmarks at 0 warnings. Interim rules name the row that removes them.

| Row | Change | Failing test first |
|---|---|---|
| D0 | The guards that exist before the interface does: the three ratchets (20, 32, 15/14) and `NamingConventions_LiveInOneHelper`. Tests only, no production edit. | A scratch 21st `IsRequired` reference, a scratch `OfType<Entity>().FirstOrDefault` and a scratch `"not_"` each fail their ratchet (recorded, then removed). |
| D1 | `IDomainModel`, `IDomainModelRule`, the rule registry, and `analysis.GetDomainModel(domain)` over today's bags; the structural guards in §6. **Interim:** `Find*` delegate to today's lookup extensions. Removed by D6. | `GetDomainModel` over an analysis that skipped the catalog pass throws; two models over one result answer equally. |
| D2 | **The 20 `IsRequired` call sites** move to `model.IsRequired(…)` via `IRequirednessRule`, registered by the core library. Lowering's 3 sites read the model, which settles the CORE.md line. Ratchet 20 → 0. | The ratchet is lowered first (red). A pack-registered requiredness rule changes `Create`'s required slots (fails today: no seam). |
| D3 | One door per kind (#953). D3a: analysis sites. D3b: lowering sites, including `DomainExpressionLoweringPass.cs:149,168,187,288`. D3c: `Queries`, `Poly.Mcp`. The 5 private copies and `FindStage` are deleted; `FindAction` holds the stage-copy rule once. Each site that silently skipped on null and now fails closed is a behaviour change: listed in the PR with a test (Q5). Ratchet 32 → 0. | The ratchet (red); per changed site, a domain whose lookup misses and today is skipped. |
| D4 | Vocabulary (#952): `StageEnumName`, `NegatedPolicyName`, `NegatedPolicyBase`. Every `"not_"` and `{Entity}Stage` literal goes through them. Closes #890's `{Other}Stage` collision only if the helper can see the domain's enums (Q4). | `NamingConventions_LiveInOneHelper` (red). |
| D5 | `IStageShapeRule`, `ICreateSlotRule`, and the entry-assigned / assign-only name sets move off `EntityStructureAnalyzer`, which keeps only the bag it publishes. | The remaining static helpers' external references to 0 (ratchet, red). |
| D6 | Delete the lookup extensions and the 5 lookup getters' public reach; the 3 dead doors go. **Public contract change; fresh-context review from another model family.** Update `docs/CORE.md` and the decisions README in the same change. | The ratchets and `DomainModel_IsExactlyTheDecidedMembers` already green; a guard that `DomainSemanticLookupExtensions` is gone. |

Not in scope: the expression-kind facts table and shared `Children()` traversal, the .NET 11 upgrade, the `Poly.Tests`
split, and the unrelated analysis metadata types (`TypedExpressionMetadata`, the contract bags). `StorageModel` is a
derived output model and stays as it is.

## 8. Risks

1. **File overlap with the expression-kind work.** D2 edits `ExpressionTypeAnalyzer.cs`, `ExpressionTypeInference.cs`,
   `ProvableGuards.cs`, `EffectInvariantAnalyzer.cs`; D3b edits `DomainExpressionLoweringPass.cs`, which also holds
   `MayBeAbsent` and `PresenceIn`. AGENTS.md allows one filler stream only if it shares no files. D0 and D1 are new
   files plus tests; D2 onward is not disjoint. Recommend D0–D1 now, D2+ after the kind-facts row merges (Q7).
2. **A model that grows a cache.** The first memoised derived fact would make it a second store. The §6 reflection
   guard exists for exactly this.
3. **Behaviour changes hidden in "one door".** A site that silently skipped on null and now fails closed changes what
   runs today. AGENTS.md requires the owner's call before a slice rejects it; D3 carries a per-site list.
4. **Public API removal (D6).** 16 of the lookup statics are public; five of them have callers in `Poly.Mcp` or `src`
   (§5) and most have test callers.
5. **Rules half is optional.** If the rules in §3 read as speculative, D2 can instead move the predicate onto an
   extension over `Property` and leave `IDomainModelRule` for a second real rule. That is a smaller doc; I have not
   recommended it because requiredness is the one rule 20 sites already share and a pack-defined answer is plausible.
6. **Counts are mine.** Regexes are ad hoc; D0 fixes their definitions in the tests so the ratchets cannot drift
   from the claims.

## 9. Questions for the owner

1. **Name.** `IDomainModel` (my pick) collides in meaning with `DomainTypeModel` and `DomainModelAnalyzer`.
   Alternatives: `IDomainView`, `IDomainCatalog`. Which?
2. **Requiredness: a rule or a fact?** `IsRequired` is `!Constraints.Any(OptionalConstraint)`. Should a pack be able to
   change it (registered rule, §3), or is it a fact of `Property` that belongs on an extension (risk 5)?
3. **Evolution.** `DomainMutationContext.FindEntity` (`Evolution/DomainMutationContext.cs:309`) works on a `Domain`
   before any analysis exists. Does Evolution get a Domain-only model, stay out of scope, or is that exactly the
   second store §4 forbids?
4. **Negation.** `NegatedPolicyName`/`NegatedPolicyBase` keep the `not_` string protocol behind one door. #952's other
   direction makes negation structural (a flag on the policy reference), which changes the public `Ontology` contract.
   Which end state do you want? I recommend the door first (D4) and the flag as a later row.
5. **Silent skip → fail closed.** One ruling for all D3 sites ("fail closed when the catalog exists"), or one decision
   per site? I recommend one ruling plus a per-site table in the PR.
6. **Core rule absent.** Null is "not applicable" for `ITypeSystem`. For `IRequirednessRule` and its siblings I propose
   a thrown error. Agree?
7. **Sequencing.** D0–D1 as a filler beside the expression-kind stream and D2+ after it, or hold the whole stream until
   the .NET 11 upgrade lands?
8. **Scope of D5.** Move the stage-shape and create-slot rules now, or stop after D4 and judge on what D2–D4 cost?
9. **Public removal (D6).** OK to delete the 16 public lookup statics in one slice, with the fresh-context review, or
   keep thin forwarding extensions for one release?
