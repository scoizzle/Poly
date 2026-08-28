using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Vocabulary bag: this unit loaded <c>uses temporal</c>. Checks, elaboration, and
/// lowering consume analysis — not a session Meaning table.
/// </summary>
public sealed record TemporalVocabularyMetadata : IAnalysisMetadata;

/// <summary>Registers temporal vocabulary on the domain when the library is loaded.</summary>
public sealed class TemporalPass : INodeAnalyzer {
    public const string Id = "Temporal";
    public string PassName => Id;
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain)
            return;

        context.SetMetadata(domain, new TemporalVocabularyMetadata());
        DomainAnalysis.ForEachEntity(domain, entity => AnalyzeEntity(context, entity));
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        var props = entity.Properties
            .ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);

        foreach (var prop in entity.Properties) {
            foreach (var dv in prop.Constraints.OfType<DefaultValueConstraint>()) {
                StampExpression(context, dv.Expression, entity, owningAction: null);
                CheckDefault(context, dv.Expression, prop.Type.TypeName);
                CheckDateOperations(context, dv.Expression, props, parameters: null);
            }
        }

        foreach (var policy in entity.Policies)
            CheckExprTree(context, policy.Expression, entity, props, null);
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies)
                CheckExprTree(context, policy.Expression, entity, props, null);
        }
        foreach (var action in entity.Actions)
            foreach (var policy in action.Policies)
                CheckExprTree(context, policy.Expression, entity, props, ParamsOf(action));
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                foreach (var policy in action.Policies)
                    CheckExprTree(context, policy.Expression, entity, props, ParamsOf(action));

        foreach (var action in entity.Actions)
            CheckEffects(context, action.Effects, entity, action, props, ParamsOf(action));
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                CheckEffects(context, action.Effects, entity, action, props, ParamsOf(action));
            CheckEffects(context, stage.OnEntryEffects, entity, owningAction: null, props, null);
            CheckEffects(context, stage.OnExitEffects, entity, owningAction: null, props, null);
            foreach (var sub in stage.Subscriptions)
                CheckEffects(context, sub.Effects, entity, owningAction: null, props, null);
        }
        foreach (var sub in entity.Subscriptions)
            CheckEffects(context, sub.Effects, entity, owningAction: null, props, null);
    }

    private static Dictionary<string, string>? ParamsOf(Action action) =>
        action.Parameters.Count > 0
            ? action.Parameters.ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal)
            : null;

    private static void CheckEffects(
        AnalysisContext context,
        IEnumerable<Effect> effects,
        Entity entity,
        Action? owningAction,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters) {
        foreach (var effect in EffectHelpers.FlattenEffects(effects)) {
            if (effect is AssignEffect assign) {
                StampExpression(context, assign.Value, entity, owningAction);
                StampExpression(context, assign.Target, entity, owningAction);
                CheckDateOperations(context, assign.Value, props, parameters);
                CheckBareDuration(context, assign.Value);
                StampAssignConversion(context, assign, entity, owningAction);
            }
            else {
                foreach (var expr in effect.Children.OfType<DomainExpression>()) {
                    StampExpression(context, expr, entity, owningAction);
                    CheckDateOperations(context, expr, props, parameters);
                    CheckBareDuration(context, expr);
                }
            }
        }
    }

    private static void CheckExprTree(
        AnalysisContext context,
        DomainExpression expr,
        Entity entity,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters) {
        StampExpression(context, expr, entity, owningAction: null);
        CheckDateOperations(context, expr, props, parameters);
        CheckBareDuration(context, expr);
    }

    private static void StampExpression(
        AnalysisContext context,
        DomainExpression expr,
        Entity entity,
        Action? owningAction) {
        foreach (var child in expr.Children.OfType<DomainExpression>())
            StampExpression(context, child, entity, owningAction);

        switch (expr) {
            case Now:
                context.SetMetadata(expr, new CatalogTypedExpressionMetadata("DateTime"));
                break;
            case Today:
                context.SetMetadata(expr, new CatalogTypedExpressionMetadata("Date"));
                break;
            case Duration:
                context.SetMetadata(expr, new CatalogTypedExpressionMetadata("Duration"));
                break;
            case DateOperation d:
                var name = TypeNameOf(d.Date, entity, owningAction, context);
                if (name is not null)
                    context.SetMetadata(expr, new CatalogTypedExpressionMetadata(name));
                break;
        }
    }

    private static void CheckDateOperations(
        AnalysisContext context,
        DomainExpression expr,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters) {
        if (expr is DateOperation dateOp)
            CheckDateOperation(context, dateOp, props, parameters);
        foreach (var child in expr.Children.OfType<DomainExpression>())
            CheckDateOperations(context, child, props, parameters);
    }

    private static void CheckBareDuration(AnalysisContext context, DomainExpression expr) {
        if (expr is Duration d) {
            context.ReportError(expr,
                $"default value '{d.Amount} {d.Unit}' is a bare duration with no temporal left operand",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            return;
        }
        foreach (var child in expr.Children.OfType<DomainExpression>())
            CheckBareDuration(context, child);
    }

    private static void CheckDefault(AnalysisContext context, DomainExpression expr, string propTypeName) {
        var dateLike = propTypeName is "Date" or "DateOnly" or "DateTime" or "Timestamp";
        switch (expr) {
            case Now:
                if (!dateLike)
                    context.ReportError(expr,
                        $"default(Now) is not compatible with property type '{propTypeName}' " +
                        "(use a date property, or 'Guid' for identifiers)",
                        DomainModelDiagnosticCodes.SemanticTypeCompatibility);
                break;
            case Today:
                if (!dateLike)
                    context.ReportError(expr,
                        $"default(Today) is not compatible with property type '{propTypeName}' " +
                        "(use a date property, or 'Guid' for identifiers)",
                        DomainModelDiagnosticCodes.SemanticTypeCompatibility);
                break;
            case Duration d:
                context.ReportError(expr,
                    $"default value '{d.Amount} {d.Unit}' is a bare duration with no temporal left operand",
                    DomainModelDiagnosticCodes.SemanticTypeCompatibility);
                break;
        }
    }

    private static void CheckDateOperation(
        AnalysisContext context,
        DateOperation dateOp,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters) {
        var dateName = TypeNameOf(dateOp.Date, props, parameters, context);
        var dateLike = dateOp.Date is Now or Today or DateOperation
            || dateName is "Date" or "DateOnly" or "DateTime" or "Timestamp";
        var timeLike = dateName is "Time" or "TimeOnly";
        if (dateName is null && !dateLike && !timeLike)
            return;
        if (!dateLike && !timeLike) {
            context.ReportError(dateOp,
                $"temporal offset requires a date left operand (got '{Describe(dateName)}'); " +
                "a duration needs a date or clock node ('Now'/'Today') to offset",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            return;
        }

        var clockTyped = dateOp.Date is Now
            || dateName is "DateTime" or "Timestamp" or "Time" or "TimeOnly";
        if (DurationForm.IsClockResolution(dateOp.Kind) && !clockTyped) {
            context.ReportError(dateOp,
                $"clock-resolution duration ({DurationForm.Spell(dateOp.Kind)}) requires Now, DateTime, or Time " +
                $"(got '{Describe(dateName)}'); Date/Today have no time of day",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
        else if (DurationForm.IsCalendarResolution(dateOp.Kind) && timeLike) {
            context.ReportError(dateOp,
                $"calendar duration ({DurationForm.Spell(dateOp.Kind)}) requires a date or DateTime " +
                $"(got '{Describe(dateName)}'); Time has no calendar date",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static void StampAssignConversion(
        AnalysisContext context,
        AssignEffect assign,
        Entity entity,
        Action? owningAction) {
        if (assign.Target is not PropertyAccess dest)
            return;
        var destType = TypeOfProperty(entity, dest.Name);
        if (destType is null || !IsDateTimeName(destType))
            return;
        if (!IsDateTypedRhs(assign.Value, entity, owningAction))
            return;

        context.SetMetadata(assign, new AssignedMemberConversionMetadata(
            "ToDateTime",
            [new AssignedMemberConversionArgument("TimeOnly", "MinValue")]));
    }

    private static bool IsDateTypedRhs(DomainExpression value, Entity entity, Action? owningAction) =>
        value switch {
            Today => true,
            Now => false,
            PropertyAccess pa =>
                (TypeOfProperty(entity, pa.Name) ?? TypeOfParameter(owningAction, pa.Name)) is { } t
                && IsDateName(t),
            ParameterAccess param => TypeOfParameter(owningAction, param.Name) is { } t && IsDateName(t),
            DateOperation d => IsDateTypedRhs(d.Date, entity, owningAction),
            _ => false,
        };

    private static string? TypeNameOf(
        DomainExpression expr,
        Entity entity,
        Action? owningAction,
        AnalysisContext context) {
        if (context.GetMetadata<CatalogTypedExpressionMetadata>(expr) is { TypeName: { } stamped })
            return stamped;
        return expr switch {
            Now => "DateTime",
            Today => "Date",
            PropertyAccess pa => TypeOfProperty(entity, pa.Name) ?? TypeOfParameter(owningAction, pa.Name),
            ParameterAccess param => TypeOfParameter(owningAction, param.Name),
            DateOperation d => TypeNameOf(d.Date, entity, owningAction, context),
            _ => null,
        };
    }

    private static string? TypeNameOf(
        DomainExpression expr,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        AnalysisContext context) {
        if (context.GetMetadata<CatalogTypedExpressionMetadata>(expr) is { TypeName: { } stamped })
            return stamped;
        return expr switch {
            Now => "DateTime",
            Today => "Date",
            PropertyAccess pa => Resolve(pa.Name, props, parameters),
            ParameterAccess param => Resolve(param.Name, props, parameters),
            DateOperation d => TypeNameOf(d.Date, props, parameters, context),
            _ => null,
        };
    }

    private static string? Resolve(string name, Dictionary<string, string> props, Dictionary<string, string>? parameters) {
        if (props.TryGetValue(name, out var pt)) return pt;
        if (parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    private static string Describe(string? typeName) => typeName switch {
        "Date" or "DateOnly" => "Date (Date)",
        "DateTime" or "Timestamp" => "Date (DateTime)",
        "Time" or "TimeOnly" => "Time (Time)",
        null => "unknown",
        _ => typeName,
    };

    private static string? TypeOfProperty(Entity entity, string propertyName) =>
        entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal))?.Type.TypeName;

    private static string? TypeOfParameter(Action? action, string name) =>
        action?.Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal))?.Type.TypeName;

    private static bool IsDateName(string typeName) => typeName is "Date" or "DateOnly";

    private static bool IsDateTimeName(string typeName) => typeName is "DateTime" or "Timestamp";
}