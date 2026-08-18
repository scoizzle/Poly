using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared context for lowering passes. Bundles the subject (current-instance root),
/// optional parameter map, and analysis metadata so lowering passes can consume
/// pre-computed bags instead of re-scanning <see cref="Domain"/> collections.
///
/// When <see cref="Analysis"/> is provided, lowering reads <see cref="IAnalysisMetadata"/>
/// via <see cref="INodeMetadataProvider.GetMetadata{T}"/> (typically an
/// <see cref="AnalysisResult"/>). When null, lowering falls back to re-scan logic.
/// </summary>
/// <param name="Subject">The Syntax AST node representing the current entity instance.</param>
/// <param name="Parameters">
/// Optional map of parameter names to Syntax AST nodes. Used for
/// <c>ParameterAccess</c> and for path-prefix roots that should resolve as
/// parameter subjects (e.g. peer binders in C# subscription handlers:
/// <c>order Code</c> → <c>order.Code</c>, not <c>this.order.Code</c>).
/// Does not rewrite bag values — VM peer binding remains a separate pre-lower rewrite.
/// </param>
/// <param name="Analysis">
/// Metadata provider with pre-computed analysis bags. When present, lowering uses
/// provider lookups instead of scanning domain collections. Null-safe (falls
/// back to re-scan).</param>
/// <param name="UseThisReference">
/// When true, the lowered tree uses <see cref="ThisReference"/> as the instance root
/// instead of <see cref="Parameter"/>. Useful when generating C# method bodies where
/// <c>this.Property</c> is idiomatic. Defaults to false (VM-compatible mode).
/// </param>
/// <param name="ActionParameterNames">
/// When <see cref="UseThisReference"/> is true, these names are rendered as bare
/// parameters (e.g. <c>maxAmount</c>) instead of <c>this.maxAmount</c>.
/// </param>
/// <param name="LowerStageTransitions">
/// When true, <see cref="StageTransitionEffect"/> is lowered to an Assignment
/// node (<c>this.CurrentStage = Xxx</c>) instead of returning null for direct
/// execution. Used for C# code generation where transitions should be emitted
/// as property writes. Defaults to false (runtime-compatible mode).
/// </param>
/// <param name="Domain">Optional domain reference for cross-entity type resolution.</param>
/// <param name="StageEnumTypeName">
/// Optional stage enum type name for stage transition lowering. Overrides the
/// default <c>{EntityName}Stage</c> derivation — necessary for inherited entities
/// where the stage enum is defined on the root ancestor.
/// </param>
/// <param name="PostTransitionNodes">
/// Optional map of stage name to Syntax AST nodes to emit <em>after</em> the
/// <c>CurrentStage</c> assignment when lowering a transition to that stage.
/// Used for cross-entity subscription notifications in C# codegen mode.
/// </param>
/// <param name="SourceStageName">
/// Optional name of the source stage from which a transition originates.
/// When set, exit effects of the source stage are emitted before the
/// target stage's entry effects.
/// </param>
/// <param name="EnumPropertyNames">
/// Optional map from property name to enum type name. When present, literal
/// comparisons against enum-typed properties emit qualified member access
/// (e.g. <c>PatronStatus.Active</c>) instead of string literals.
/// </param>
/// <param name="NavigationNameResolver">
/// Optional mapper from a DSL relationship/navigation name to the generated C#
/// member name. The exporter emits pascal-cased nav properties
/// (<c>compilations</c> → <c>Compilations</c>) while DSL expressions use the
/// camelCase name; this resolver is the single source of truth so expression
/// lowering (property reads, <c>Rel exists</c>, path-prefix) and the exporter
/// agree on the member name. Falls back to identity when null.
/// </param>
/// <param name="IsCollectionNavigation">
/// Optional predicate answering whether a DSL relationship/navigation name is a
/// collection (<c>many</c>) on the current subject entity. The C# export uses it
/// to lower <c>Rel exists</c> to a <c>.Count != 0</c> check (runtime store-link
/// presence) instead of a never-null <c>collection != null</c>.
/// </param>
/// <param name="PropertyTypeResolver">
/// Optional mapper from a property name to its domain type name. Used to lower
/// date arithmetic (<c>DueDate + 14</c> → <c>DueDate.AddDays(...)</c>) in every
/// expression context (policies, if conditions, initializers), not just assign.
/// </param>
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null,
    INodeMetadataProvider? Analysis = null,
    bool UseThisReference = false,
    HashSet<string>? ActionParameterNames = null,
    bool LowerStageTransitions = false,
    Domain? Domain = null,
    string? StageEnumTypeName = null,
    IReadOnlyDictionary<string, IReadOnlyList<Node>>? PostTransitionNodes = null,
    string? SourceStageName = null,
    IReadOnlyDictionary<string, string>? EnumPropertyNames = null,
    Func<string, string>? NavigationNameResolver = null,
    Func<string, bool>? IsCollectionNavigation = null,
    Func<string, string?>? PropertyTypeResolver = null,
    ExpressionMeaning? Meaning = null
);