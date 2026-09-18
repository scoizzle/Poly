using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Session interception for temporal vocab: inference, type checks, assign conversions.
/// Core passes read <see cref="ExpressionMeaning"/>; this is not an analysis pass.
/// </summary>
public static class TemporalMeaning {
    public static void Register(ExpressionMeaning meaning) {
        ArgumentNullException.ThrowIfNull(meaning);
        meaning.Inference.Register(new NowInference());
        meaning.Inference.Register(new TodayInference());
        meaning.Inference.Register(new DurationInference());
        meaning.Inference.Register(new DateOperationInference());
        meaning.Checks.Register(new NowDefaultCheck());
        meaning.Checks.Register(new TodayDefaultCheck());
        meaning.Checks.Register(new DurationCheck());
        meaning.Checks.Register(new DateOperationCheck());
        meaning.RegisterAssignConversion(new DateToDateTimeConversion());
        meaning.RegisterAssignConversion(new ClockAssignCompatibility());
        TemporalLowering.Register(meaning);
    }

    private sealed class NowInference : IExpressionDispatchHandler<string> {
        public Type ExpressionType => typeof(Now);
        public bool TryHandle(DomainExpression expression, Func<DomainExpression, string> route, out string result) {
            result = "DateTime";
            return true;
        }
    }

    private sealed class TodayInference : IExpressionDispatchHandler<string> {
        public Type ExpressionType => typeof(Today);
        public bool TryHandle(DomainExpression expression, Func<DomainExpression, string> route, out string result) {
            result = "Date";
            return true;
        }
    }

    private sealed class DurationInference : IExpressionDispatchHandler<string> {
        public Type ExpressionType => typeof(Duration);
        public bool TryHandle(DomainExpression expression, Func<DomainExpression, string> route, out string result) {
            result = "Duration";
            return true;
        }
    }

    private sealed class DateOperationInference : IExpressionDispatchHandler<string> {
        public Type ExpressionType => typeof(DateOperation);
        public bool TryHandle(DomainExpression expression, Func<DomainExpression, string> route, out string result) {
            var name = route(((DateOperation)expression).Date);
            if (string.IsNullOrEmpty(name)) {
                result = null!;
                return false;
            }
            result = name;
            return true;
        }
    }

    private sealed class NowDefaultCheck : IExpressionTypeCheck {
        public Type ExpressionType => typeof(Now);
        public void Check(AnalysisContext context, DomainExpression expression, ExpressionTypeCheckScope scope) {
            if (scope.DefaultTargetTypeName is not { } propTypeName)
                return;
            if (propTypeName is "Date" or "DateOnly" or "DateTime" or "Timestamp")
                return;
            context.ReportError(expression,
                $"default(Now) is not compatible with property type '{propTypeName}' " +
                "(use a date property, or 'Guid' for identifiers)",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private sealed class TodayDefaultCheck : IExpressionTypeCheck {
        public Type ExpressionType => typeof(Today);
        public void Check(AnalysisContext context, DomainExpression expression, ExpressionTypeCheckScope scope) {
            if (scope.DefaultTargetTypeName is not { } propTypeName)
                return;
            if (propTypeName is "Date" or "DateOnly" or "DateTime" or "Timestamp")
                return;
            context.ReportError(expression,
                $"default(Today) is not compatible with property type '{propTypeName}' " +
                "(use a date property, or 'Guid' for identifiers)",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private sealed class DurationCheck : IExpressionTypeCheck {
        public Type ExpressionType => typeof(Duration);
        public void Check(AnalysisContext context, DomainExpression expression, ExpressionTypeCheckScope scope) {
            if (expression is not Duration d)
                return;
            context.ReportError(expression,
                $"default value '{d.Amount} {d.Unit}' is a bare duration with no temporal left operand",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private sealed class DateOperationCheck : IExpressionTypeCheck {
        public Type ExpressionType => typeof(DateOperation);
        public void Check(AnalysisContext context, DomainExpression expression, ExpressionTypeCheckScope scope) {
            if (expression is not DateOperation dateOp)
                return;
            var dateName = TypeNameOf(dateOp.Date, scope, context);
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
    }

    /// <summary>
    /// Now/Today may land on Date or DateTime; lowering adapts. Core Compatible
    /// would reject DateTime-inferred Now onto Date.
    /// </summary>
    private sealed class ClockAssignCompatibility : IAssignConversionAdvisor {
        public bool TryAdvise(AnalysisContext context, AssignEffect assign, ExpressionTypeCheckScope scope) =>
            false;

        public bool TryClaimAssign(
            AnalysisContext context,
            DomainExpression value,
            string targetTypeName,
            ExpressionTypeCheckScope scope) {
            if (value is not (Now or Today))
                return false;
            return targetTypeName is "Date" or "DateOnly" or "DateTime" or "Timestamp";
        }
    }

    private sealed class DateToDateTimeConversion : IAssignConversionAdvisor {
        public bool TryAdvise(AnalysisContext context, AssignEffect assign, ExpressionTypeCheckScope scope) {
            if (assign.Target is not PropertyAccess dest)
                return false;
            if (!scope.Properties.TryGetValue(dest.Name, out var destType) &&
                (scope.Parameters is null || !scope.Parameters.TryGetValue(dest.Name, out destType)))
                return false;
            if (destType is not ("DateTime" or "Timestamp"))
                return false;
            if (!IsDateTypedRhs(assign.Value, scope, context))
                return false;
            context.SetMetadata(assign, new AssignedMemberConversionMetadata(
                "ToDateTime",
                [new AssignedMemberConversionArgument("TimeOnly", "MinValue")]));
            return true;
        }
    }

    private static bool IsDateTypedRhs(
        DomainExpression value, ExpressionTypeCheckScope scope, AnalysisContext context) =>
        value switch {
            Today => true,
            Now => false,
            PropertyAccess pa => TypeNameOf(pa, scope, context) is "Date" or "DateOnly",
            ParameterAccess param => TypeNameOf(param, scope, context) is "Date" or "DateOnly",
            DateOperation d => IsDateTypedRhs(d.Date, scope, context),
            _ => false,
        };

    private static string? TypeNameOf(
        DomainExpression expr, ExpressionTypeCheckScope scope, AnalysisContext context) {
        if (context.GetMetadata<CatalogTypedExpressionMetadata>(expr) is { TypeName: { } stamped })
            return stamped;
        return expr switch {
            Now => "DateTime",
            Today => "Date",
            PropertyAccess pa => Resolve(pa.Name, scope),
            ParameterAccess param => Resolve(param.Name, scope),
            DateOperation d => TypeNameOf(d.Date, scope, context),
            _ => null,
        };
    }

    private static string? Resolve(string name, ExpressionTypeCheckScope scope) {
        if (scope.Properties.TryGetValue(name, out var pt))
            return pt;
        if (scope.Parameters?.TryGetValue(name, out var ptype) == true)
            return ptype;
        return null;
    }

    private static string Describe(string? typeName) => typeName switch {
        "Date" or "DateOnly" => "Date (Date)",
        "DateTime" or "Timestamp" => "Date (DateTime)",
        "Time" or "TimeOnly" => "Time (Time)",
        null => "unknown",
        _ => typeName,
    };
}