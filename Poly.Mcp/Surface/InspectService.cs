using System.Text;
using System.Text.Json.Serialization;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Mcp.Surface;

/// <summary>What <c>poly_inspect</c> answers: ok with a summary and data, or an error with an optional code.</summary>
internal sealed record InspectOutcome(bool Ok, string Summary, object? Data, string? ErrorCode = null);

internal sealed record DescribeExpressionData(
    [property: JsonPropertyName("structured")] string Structured,
    [property: JsonPropertyName("plainEnglish")] string PlainEnglish);

/// <summary>One inspected element. <c>kind</c> is the lowerCamel <see cref="ElementKind"/>; the extras appear only for the kinds that have them.</summary>
internal sealed record InspectedElement(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parentEntity")] string? ParentEntity,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("expression")] DescribeExpressionData? Expression = null) {
    [JsonPropertyName("path"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonPropertyName("stage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stage { get; init; }

    [JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    [JsonPropertyName("constraints"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Constraints { get; init; }

    [JsonPropertyName("target"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }

    [JsonPropertyName("cardinality"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cardinality { get; init; }

    [JsonPropertyName("members"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Members { get; init; }

    [JsonPropertyName("properties"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Properties { get; init; }

    [JsonPropertyName("parameters"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Parameters { get; init; }

    [JsonPropertyName("returnType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnType { get; init; }
}

internal sealed record ElementMatchData(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("path")] string Path);

internal sealed record ElementNotFoundData(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("candidates")] IReadOnlyList<string> Candidates) {
    [JsonPropertyName("partial"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Partial { get; init; }

    [JsonPropertyName("matches"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ElementMatchData>? Matches { get; init; }
}

/// <summary>
/// <c>poly_inspect</c>: parse one <see cref="ElementPath"/>, resolve it once through
/// <see cref="ElementPathResolver"/>, describe the element. The resolver is pure over the domain; the
/// analysis is consulted only for the kinds whose detail needs it, and a missing analysis or metadata
/// bag is its own error, never "not found".
/// </summary>
internal static class InspectService {
    public const string ElementNotFoundCode = "ELEMENT_NOT_FOUND";

    public static InspectOutcome Inspect(string workspace, McpSessionState state, string? select, QueryTool query) {
        if (string.IsNullOrWhiteSpace(select))
            return FromResponse(query.GetDomainOverview(workspace));

        if (!ElementPath.TryParse(select, out var path, out var parseError)) {
            return new InspectOutcome(false, $"The select path is malformed: {parseError}",
                new ElementNotFoundData("unknown", []), ElementNotFoundCode);
        }

        return ElementPathResolver.Resolve(state.Domain, path!) switch {
            ElementRef element => Describe(workspace, state, element, query),
            ElementAmbiguous ambiguous => Ambiguous(select, ambiguous),
            ElementNotFound notFound => NotFound(notFound),
            _ => throw new InvalidOperationException("Unknown element resolution."),
        };
    }

    private static InspectOutcome FromResponse(DomainToolResponse response) =>
        new(response.Success, response.Message, response.Data);

    private static InspectOutcome NotFound(ElementNotFound notFound) =>
        new(false, notFound.Message,
            new ElementNotFoundData(notFound.Reason == NotFoundReason.Unknown ? "unknown" : "notAddressable", notFound.Candidates) {
                Partial = notFound.Partial?.Canonical,
            },
            ElementNotFoundCode);

    private static InspectOutcome Ambiguous(string select, ElementAmbiguous ambiguous) {
        // An entity-level action's bare path is the ambiguous one, so it is offered in the exact
        // (analysis-path) form, which selects only that action.
        var matches = ambiguous.Matches
            .Select(m => new ElementMatchData(KindName(m.Kind), m.Kind == ElementKind.EntityAction ? m.AnalysisPath : m.Canonical))
            .ToList();
        var sameName = matches.Select(static m => m.Path).Distinct(StringComparer.Ordinal).Count() < matches.Count;
        var summary = $"'{select}' is ambiguous: it matches "
            + string.Join("; ", matches.Select(m => $"{m.Kind} {m.Path}"))
            + (sameName
                ? ". Two of them share one path, so no path form selects one; rename one of the elements."
                : ". Pass the full path to pick one.");
        IReadOnlyList<string> candidates = sameName
            ? [.. matches.Select(static m => $"{m.Kind} {m.Path}")]
            : [.. matches.Select(static m => m.Path)];
        return new InspectOutcome(false, summary,
            new ElementNotFoundData("ambiguous", candidates) { Matches = matches },
            ElementNotFoundCode);
    }

    private static InspectOutcome Describe(string workspace, McpSessionState state, ElementRef element, QueryTool query) {
        var domain = state.Domain;
        return element.Kind switch {
            ElementKind.Domain => FromResponse(query.GetDomainOverview(workspace)),
            ElementKind.Entity => FromResponse(query.GetEntityDetail(workspace, element.Name)),
            ElementKind.Property => DescribeProperty(element, (Property)element.Node!),
            ElementKind.Enum => DescribeEnum(element, (EnumType)element.Node!),
            ElementKind.EnumMember => DescribeEnumMember(element, domain),
            ElementKind.ValueType => DescribeValueType(element, (ValueType)element.Node!),
            ElementKind.Function => DescribeFunction(element, (DomainFunction)element.Node!),
            ElementKind.Navigation => DescribeNavigation(workspace, state, element, (Relationship)element.Node!),
            ElementKind.Stage => DescribeStage(workspace, state, element, (Stage)element.Node!),
            ElementKind.EntityAction or ElementKind.StageAction => DescribeAction(workspace, state, element, (Poly.DomainModeling.Ontology.Action)element.Node!),
            ElementKind.Policy => DescribePolicy(workspace, state, element, (Policy)element.Node!),
            _ => throw new InvalidOperationException($"Unhandled element kind {element.Kind}."),
        };
    }

    // ── Pure-domain kinds ───────────────────────────────────────

    private static InspectOutcome DescribeProperty(ElementRef element, Property property) {
        var constraints = DomainPresentation.Constraints(property);
        var summary = $"Property '{element.Name}' on entity '{element.Entity}': {property.Type.TypeName}"
            + (constraints is null ? "" : $" ({constraints})") + ".";
        return Ok(element, summary, e => e with { Type = property.Type.TypeName, Constraints = constraints });
    }

    private static InspectOutcome DescribeEnum(ElementRef element, EnumType enumType) {
        var summary = $"Enum '{element.Name}' with {enumType.MemberNames.Count} member(s): {string.Join(", ", enumType.MemberNames)}.";
        return Ok(element, summary, e => e with { Members = [.. enumType.MemberNames] });
    }

    private static InspectOutcome DescribeEnumMember(ElementRef element, Domain domain) {
        var parent = element.Canonical[..element.Canonical.LastIndexOf('.')];
        var summary = $"Member '{element.Name}' of enum '{parent}' ({domain.Name}).";
        return Ok(element, summary);
    }

    private static InspectOutcome DescribeValueType(ElementRef element, ValueType valueType) {
        var names = valueType.Properties.Select(static p => p.Name).ToList();
        var summary = $"Value type '{element.Name}' with {names.Count} propert{(names.Count == 1 ? "y" : "ies")}: {string.Join(", ", names)}.";
        return Ok(element, summary, e => e with { Properties = names });
    }

    private static InspectOutcome DescribeFunction(ElementRef element, DomainFunction function) {
        var parameters = function.Parameters.Select(static p => p.Name).ToList();
        var signature = string.Join(", ", function.Parameters.Select(static p => $"{p.Name}: {p.Type.TypeName}"));
        var summary = $"Function '{element.Name}({signature})': {function.ReturnType.TypeName}.";
        return Ok(element, summary, e => e with { Parameters = parameters, ReturnType = function.ReturnType.TypeName });
    }

    // ── Kinds whose detail needs the analysis ───────────────────

    private static InspectOutcome DescribeNavigation(string workspace, McpSessionState state, ElementRef element, Relationship relationship) {
        if (state.LatestAnalysis is null)
            return NoAnalysis(workspace);
        if (state.LatestAnalysis.GetRelationshipLookup(state.Domain) is null)
            return new InspectOutcome(false, $"Session analysis is missing DomainCatalogMetadata (relationship lookup) required to describe relationship '{element.Name}'.", null);

        var cardinality = relationship.Cardinality switch {
            RelationshipCardinality.OneToOne => "one-to-one",
            RelationshipCardinality.OneToMany => "one-to-many",
            RelationshipCardinality.ManyToMany => "many-to-many",
            _ => relationship.Cardinality.ToString(),
        };
        var sb = new StringBuilder();
        sb.Append($"Relationship '{element.Name}': {relationship.Source.TypeName} → {relationship.Target.TypeName} ({cardinality})");
        if (relationship.SourceOwnsTarget)
            sb.Append(", source owns target");
        if (relationship.Stages.Count > 0)
            sb.Append($", {relationship.Stages.Count} stage(s)");
        return Ok(element, sb.ToString(), e => e with { Target = relationship.Target.TypeName, Cardinality = cardinality });
    }

    private static InspectOutcome DescribeStage(string workspace, McpSessionState state, ElementRef element, Stage stage) {
        var analysis = state.LatestAnalysis;
        if (analysis is null)
            return NoAnalysis(workspace);

        var entity = state.Domain.Types.OfType<Entity>().First(e => string.Equals(e.Name, element.Entity, StringComparison.Ordinal));
        if (analysis.GetStructure(entity) is null)
            return new InspectOutcome(false, $"Session analysis is missing EntityStructureMetadata required to describe stage '{element.Name}'.", null);

        var effectiveActionCount = analysis.GetEffectiveActions(state.Domain, entity, element.Name).Count;
        var effectivePolicyCount = analysis.GetEffectivePolicies(state.Domain, entity, element.Name).Count;
        var sb = new StringBuilder();
        sb.Append($"Stage '{element.Name}' on entity '{entity.Name}' with {effectiveActionCount} effective actions, {effectivePolicyCount} effective policies, {stage.Subscriptions.Count} subscriptions.");
        if (stage.OnEntryEffects.Count > 0)
            sb.Append($" Has {stage.OnEntryEffects.Count} entry effect(s).");
        if (stage.OnExitEffects.Count > 0)
            sb.Append($" Has {stage.OnExitEffects.Count} exit effect(s).");
        if (effectiveActionCount > stage.Actions.Count)
            sb.Append($" Includes {effectiveActionCount - stage.Actions.Count} inherited action(s).");
        return Ok(element, sb.ToString());
    }

    private static InspectOutcome DescribeAction(string workspace, McpSessionState state, ElementRef element, Poly.DomainModeling.Ontology.Action action) {
        var analysis = state.LatestAnalysis;
        if (analysis is null)
            return NoAnalysis(workspace);
        if (analysis.GetCatalog(state.Domain) is null)
            return new InspectOutcome(false, $"Session analysis is missing DomainCatalogMetadata required to describe action '{element.Name}'.", null);

        var transitionTargets = analysis.GetMetadata<ActionCapabilityMetadata>(action)?.View.TransitionTargets.Select(static t => t.Name).ToList() ?? [];
        var sb = new StringBuilder();
        sb.Append($"Action '{element.Name}' on entity '{element.Entity}'");
        if (element.Stage is not null)
            sb.Append($" in stage '{element.Stage}'");
        if (action.Parameters.Count > 0)
            sb.Append($" with parameters: {string.Join(", ", action.Parameters.Select(static p => $"{p.Name}: {p.Type.TypeName}"))}");
        sb.Append($", {action.Effects.Count} effect(s), {action.Policies.Count} guard(s).");
        if (transitionTargets.Count > 0)
            sb.Append($" Transitions to: {string.Join(", ", transitionTargets)}.");
        return Ok(element, sb.ToString());
    }

    private static InspectOutcome DescribePolicy(string workspace, McpSessionState state, ElementRef element, Policy policy) {
        var analysis = state.LatestAnalysis;
        if (analysis is null)
            return NoAnalysis(workspace);
        if (analysis.GetCatalog(state.Domain) is null)
            return new InspectOutcome(false, $"Session analysis is missing DomainCatalogMetadata required to describe policy '{element.Name}'.", null);

        var entity = state.Domain.Types.OfType<Entity>().First(e => string.Equals(e.Name, element.Entity, StringComparison.Ordinal));
        var expression = DescribeExpression(policy.Expression);
        var sb = new StringBuilder();
        sb.Append($"Policy '{element.Name}' on entity '{entity.Name}' ({ScopeOf(entity, policy)} scope): {expression.PlainEnglish}.");
        return Ok(element, sb.ToString(), expression: expression);
    }

    private static string ScopeOf(Entity entity, Policy policy) {
        if (entity.Policies.Any(p => ReferenceEquals(p, policy)))
            return "entity";
        var stage = entity.Stages.FirstOrDefault(s => s.Policies.Any(p => ReferenceEquals(p, policy)));
        if (stage is not null)
            return $"stage '{stage.Name}'";
        var action = entity.Actions.Concat(entity.Stages.SelectMany(static s => s.Actions))
            .First(a => a.Policies.Any(p => ReferenceEquals(p, policy)));
        return $"action '{action.Name}'";
    }

    // ── Shaping ─────────────────────────────────────────────────

    private static InspectOutcome NoAnalysis(string workspace) =>
        new(false, $"Session '{workspace}' has no analysis. Apply a domain first (apply_dsl or evolution).", null);

    private static string KindName(ElementKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    private static InspectOutcome Ok(
        ElementRef element,
        string summary,
        Func<InspectedElement, InspectedElement>? shape = null,
        DescribeExpressionData? expression = null) {
        var data = new InspectedElement(KindName(element.Kind), element.Name, element.Entity, summary, summary, expression) {
            Path = element.Canonical,
            Stage = element.Stage,
        };
        return new InspectOutcome(true, summary, shape is null ? data : shape(data));
    }

    // ── Expression description (moved from OracleTool with the policy detail) ──

    private static DescribeExpressionData DescribeExpression(DomainExpression expr) =>
        new(DescribeStructured(expr, 0), DescribePlainEnglish(expr));

    private static string DescribeStructured(DomainExpression expr, int indent) {
        var pad = new string(' ', indent * 2);
        return expr switch {
            PropertyAccess p => $"{pad}Property: {p.Name}",
            ParameterAccess p => $"{pad}Parameter: {p.Name}",
            Literal l => $"{pad}Literal: {l.Value ?? "null"}",
            OwnedAccess oa => $"{pad}Owned: {oa.OwnedName}\n{DescribeStructured(oa.Inner, indent + 1)}",
            RelationshipNavigation rn => $"{pad}Nav: {rn.RelationshipName}\n{DescribeStructured(rn.TargetProperty, indent + 1)}",
            Comparison c => $"{pad}{OpName(c.Kind)}\n{DescribeStructured(c.Left, indent + 1)}\n{DescribeStructured(c.Right, indent + 1)}",
            And a => $"{pad}And\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Or o => $"{pad}Or\n{DescribeStructured(o.Left, indent + 1)}\n{DescribeStructured(o.Right, indent + 1)}",
            Not n => $"{pad}Not\n{DescribeStructured(n.Operand, indent + 1)}",
            Add a => $"{pad}Add\n{DescribeStructured(a.Left, indent + 1)}\n{DescribeStructured(a.Right, indent + 1)}",
            Subtract s => $"{pad}Subtract\n{DescribeStructured(s.Left, indent + 1)}\n{DescribeStructured(s.Right, indent + 1)}",
            Multiply m => $"{pad}Multiply\n{DescribeStructured(m.Left, indent + 1)}\n{DescribeStructured(m.Right, indent + 1)}",
            Divide d => $"{pad}Divide\n{DescribeStructured(d.Left, indent + 1)}\n{DescribeStructured(d.Right, indent + 1)}",
            Exists e => $"{pad}Exists\n{DescribeStructured(e.Target, indent + 1)}",
            NotExists ne => $"{pad}NotExists\n{DescribeStructured(ne.Target, indent + 1)}",
            DateOperation d => $"{pad}DateOp ({d.Kind})\n{DescribeStructured(d.Date, indent + 1)}\n{DescribeStructured(d.Offset, indent + 1)}",
            _ => $"{pad}{expr.GetType().Name}: {expr}"
        };
    }

    private static string DescribePlainEnglish(DomainExpression expr) => expr switch {
        PropertyAccess p => $"the value of '{p.Name}'",
        ParameterAccess p => $"parameter '{p.Name}'",
        Literal l => FormatLiteral(l.Value),
        OwnedAccess oa => $"{DescribePlainEnglish(oa.Inner)} of '{oa.OwnedName}'",
        RelationshipNavigation rn => $"{DescribePlainEnglish(rn.TargetProperty)} via '{rn.RelationshipName}'",
        Comparison c => $"{DescribePlainEnglish(c.Left)} {OpEnglish(c.Kind)} {DescribePlainEnglish(c.Right)}",
        And a => $"{DescribePlainEnglish(a.Left)} and {DescribePlainEnglish(a.Right)}",
        Or o => $"{DescribePlainEnglish(o.Left)} or {DescribePlainEnglish(o.Right)}",
        Not n => $"not ({DescribePlainEnglish(n.Operand)})",
        Add a => $"{DescribePlainEnglish(a.Left)} plus {DescribePlainEnglish(a.Right)}",
        Subtract s => $"{DescribePlainEnglish(s.Left)} minus {DescribePlainEnglish(s.Right)}",
        Multiply m => $"{DescribePlainEnglish(m.Left)} times {DescribePlainEnglish(m.Right)}",
        Divide d => $"{DescribePlainEnglish(d.Left)} divided by {DescribePlainEnglish(d.Right)}",
        Exists e => $"{DescribePlainEnglish(e.Target)} exists",
        NotExists ne => $"{DescribePlainEnglish(ne.Target)} does not exist",
        DateOperation d => $"date operation ({d.Kind}) on {DescribePlainEnglish(d.Date)} with {DescribePlainEnglish(d.Offset)}",
        _ => expr.ToString() ?? "unknown"
    };

    private static string OpName(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "==",
        ComparisonKind.NotEqual => "!=",
        ComparisonKind.LessThan => "<",
        ComparisonKind.LessThanOrEqual => "<=",
        ComparisonKind.GreaterThan => ">",
        ComparisonKind.GreaterThanOrEqual => ">=",
        _ => kind.ToString()
    };

    private static string OpEnglish(ComparisonKind kind) => kind switch {
        ComparisonKind.Equal => "equals",
        ComparisonKind.NotEqual => "does not equal",
        ComparisonKind.LessThan => "is less than",
        ComparisonKind.LessThanOrEqual => "is at most",
        ComparisonKind.GreaterThan => "is greater than",
        ComparisonKind.GreaterThanOrEqual => "is at least",
        _ => kind.ToString()
    };

    private static string FormatLiteral(object? value) => value switch {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };
}