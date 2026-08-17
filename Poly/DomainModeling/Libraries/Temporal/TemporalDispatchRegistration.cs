using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Language;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Registers temporal meaning onto a session <see cref="ExpressionMeaning"/>.
/// </summary>
public static class TemporalDispatchRegistration {
    /// <summary>Registers temporal meaning onto a session's tables. Not process-wide.</summary>
    public static void Populate(ExpressionMeaning meaning) {
        ArgumentNullException.ThrowIfNull(meaning);
        meaning.Rewrite.Register(new NowRewriteHandler());
        meaning.Rewrite.Register(new TodayRewriteHandler());
        meaning.Rewrite.Register(new DurationRewriteHandler());
        meaning.Rewrite.Register(new DateOperationRewriteHandler());

        meaning.Lowering.Register(new DateOperationLoweringHandler());
        meaning.Lowering.Register(new NowLoweringHandler());
        meaning.Lowering.Register(new TodayLoweringHandler());
        meaning.Lowering.Register(new DurationLoweringHandler());

        meaning.Inference.Register(new NowTypeHandler());
        meaning.Inference.Register(new TodayTypeHandler());
        meaning.Inference.Register(new DateOperationTypeHandler());
        meaning.Inference.Register(new DurationTypeHandler());

        meaning.Checks.Register(new DateOperationTypeCheck());
        meaning.Checks.Register(new DurationDefaultCheck());
        meaning.Checks.Register(new NowDefaultCheck());
        meaning.Checks.Register(new TodayDefaultCheck());

        meaning.Defaults.Register(new NowDefaultResolver());
        meaning.Defaults.Register(new TodayDefaultResolver());
    }
}

/// <summary>Rewrite identity: <c>Now</c> passes through unchanged.</summary>
internal sealed class NowRewriteHandler : IExpressionDispatchHandler<DomainExpression> {
    public Type ExpressionType => typeof(Now);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, DomainExpression> route, out DomainExpression result) {
        result = expression;
        return expression is Now;
    }
}

/// <summary>Rewrite identity: <c>Today</c> passes through unchanged.</summary>
internal sealed class TodayRewriteHandler : IExpressionDispatchHandler<DomainExpression> {
    public Type ExpressionType => typeof(Today);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, DomainExpression> route, out DomainExpression result) {
        result = expression;
        return expression is Today;
    }
}

/// <summary>Rewrite identity: <c>Duration</c> passes through unchanged.</summary>
internal sealed class DurationRewriteHandler : IExpressionDispatchHandler<DomainExpression> {
    public Type ExpressionType => typeof(Duration);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, DomainExpression> route, out DomainExpression result) {
        result = expression;
        return expression is Duration;
    }
}

/// <summary>Rewrite composite: <c>DateOperation</c> recurses into Date + Offset.</summary>
internal sealed class DateOperationRewriteHandler : IExpressionDispatchHandler<DomainExpression> {
    public Type ExpressionType => typeof(DateOperation);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, DomainExpression> route, out DomainExpression result) {
        if (expression is not DateOperation d) {
            result = null!;
            return false;
        }
        result = d with { Date = route(d.Date), Offset = route(d.Offset) };
        return true;
    }
}

/// <summary>Lowering: <c>Now</c> → <c>DateTime.UtcNow</c> (CLR clock mapping).</summary>
internal sealed class NowLoweringHandler : IExpressionDispatchHandler<Node> {
    public Type ExpressionType => typeof(Now);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, Node> route, out Node result) {
        if (expression is not Now) {
            result = null!;
            return false;
        }
        result = new Member(new NamedTypeReference("DateTime"), "UtcNow");
        return true;
    }
}

/// <summary>Lowering: <c>Today</c> → <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>.</summary>
internal sealed class TodayLoweringHandler : IExpressionDispatchHandler<Node> {
    public Type ExpressionType => typeof(Today);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, Node> route, out Node result) {
        if (expression is not Today) {
            result = null!;
            return false;
        }
        result = new Invoke(
            new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
            new Member(new NamedTypeReference("DateTime"), "UtcNow"));
        return true;
    }
}

/// <summary>Lowering: <c>DateOperation</c> → <c>date.AddDays/AddMonths/Subtract(offset)</c>.</summary>
internal sealed class DateOperationLoweringHandler : IExpressionDispatchHandler<Node> {
    public Type ExpressionType => typeof(DateOperation);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, Node> route, out Node result) {
        if (expression is not DateOperation d) {
            result = null!;
            return false;
        }
        var date = route(d.Date);
        var offset = route(d.Offset);
        result = d.Kind switch {
            DateOperationKind.AddDays => new Invoke(new Member(date, "AddDays"), offset),
            DateOperationKind.AddMonths => new Invoke(new Member(date, "AddMonths"), offset),
            DateOperationKind.DiffDays => new Invoke(new Member(date, "Subtract"), offset),
            _ => throw new NotSupportedException($"DateOperation kind '{d.Kind}' is not supported."),
        };
        return true;
    }
}

/// <summary>Lowering: a bare <c>Duration</c> is an unresolved temporal specialization — fail loud.</summary>
internal sealed class DurationLoweringHandler : IExpressionDispatchHandler<Node> {
    public Type ExpressionType => typeof(Duration);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, Node> route, out Node result) {
        if (expression is not Duration d) {
            result = null!;
            return false;
        }
        throw new NotSupportedException(
            $"Bare duration '{d.Amount} {d.Unit}' reached lowering without a temporal left operand " +
            "(e.g. 'Now - 12 days'). Resolve it into a DateOperation before lowering.");
    }
}

// ── Analysis inference handlers (TypeCategory) ────────────────────

/// <summary>Inference: <c>Now</c> is a Date.</summary>
internal sealed class NowTypeHandler : IExpressionDispatchHandler<ExpressionTypeAnalyzer.TypeCategory> {
    public Type ExpressionType => typeof(Now);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, ExpressionTypeAnalyzer.TypeCategory> route, out ExpressionTypeAnalyzer.TypeCategory result) {
        result = ExpressionTypeAnalyzer.TypeCategory.Date;
        return expression is Now;
    }
}

/// <summary>Inference: <c>Today</c> is a Date.</summary>
internal sealed class TodayTypeHandler : IExpressionDispatchHandler<ExpressionTypeAnalyzer.TypeCategory> {
    public Type ExpressionType => typeof(Today);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, ExpressionTypeAnalyzer.TypeCategory> route, out ExpressionTypeAnalyzer.TypeCategory result) {
        result = ExpressionTypeAnalyzer.TypeCategory.Date;
        return expression is Today;
    }
}

/// <summary>Inference: <c>DateOperation</c> is a Date.</summary>
internal sealed class DateOperationTypeHandler : IExpressionDispatchHandler<ExpressionTypeAnalyzer.TypeCategory> {
    public Type ExpressionType => typeof(DateOperation);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, ExpressionTypeAnalyzer.TypeCategory> route, out ExpressionTypeAnalyzer.TypeCategory result) {
        result = ExpressionTypeAnalyzer.TypeCategory.Date;
        return expression is DateOperation;
    }
}

/// <summary>Inference: <c>Duration</c> is a Duration (a bare specialization is invalid).</summary>
internal sealed class DurationTypeHandler : IExpressionDispatchHandler<ExpressionTypeAnalyzer.TypeCategory> {
    public Type ExpressionType => typeof(Duration);

    public bool TryHandle(DomainExpression expression, Func<DomainExpression, ExpressionTypeAnalyzer.TypeCategory> route, out ExpressionTypeAnalyzer.TypeCategory result) {
        result = ExpressionTypeAnalyzer.TypeCategory.Duration;
        return expression is Duration;
    }
}

// ── Runtime/export default resolvers ────────────────────────────

/// <summary>Resolves <c>Now</c> to <c>DateTime.UtcNow</c> (DateTime target) or
/// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> (Date target).</summary>
internal sealed class NowDefaultResolver : IExpressionDefaultResolver {
    public Type ExpressionType => typeof(Now);

    public bool TryResolve(DomainExpression expression, string? propTypeName, out object? runtimeValue, out Node exportNode) {
        if (expression is not Now) {
            runtimeValue = null;
            exportNode = null!;
            return false;
        }
        var isDateTimeTarget = propTypeName is "DateTime" or "Timestamp";
        runtimeValue = isDateTimeTarget
            ? DateTime.UtcNow
            : DateOnly.FromDateTime(DateTime.UtcNow);
        exportNode = isDateTimeTarget
            ? new Member(new NamedTypeReference("DateTime"), "UtcNow")
            : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                new Member(new NamedTypeReference("DateTime"), "UtcNow"));
        return true;
    }
}

/// <summary>Resolves <c>Today</c> to <c>DateTime.Today</c> (DateTime target) or
/// <c>DateOnly.FromDateTime(DateTime.Today)</c> (Date target).</summary>
internal sealed class TodayDefaultResolver : IExpressionDefaultResolver {
    public Type ExpressionType => typeof(Today);

    public bool TryResolve(DomainExpression expression, string? propTypeName, out object? runtimeValue, out Node exportNode) {
        if (expression is not Today) {
            runtimeValue = null;
            exportNode = null!;
            return false;
        }
        var isDateTimeTarget = propTypeName is "DateTime" or "Timestamp";
        runtimeValue = isDateTimeTarget
            ? DateTime.Today
            : DateOnly.FromDateTime(DateTime.Today);
        exportNode = isDateTimeTarget
            ? new Member(new NamedTypeReference("DateTime"), "Today")
            : new Invoke(new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                new Member(new NamedTypeReference("DateTime"), "Today"));
        return true;
    }
}

// ── Analysis check handlers (DateOperation operand + temporal defaults) ──

/// <summary>
/// Check: a <c>DateOperation</c> requires a temporal date as its date operand. The parser
/// fold is syntactic — it folds any <c>PropertyAccess</c> + <c>N days</c> before property
/// types are known — so analysis rejects a folded date operand that is a Number (or other
/// non-date) property (<c>Qty + 3 days &gt; Expiry</c> must fail closed). Unknown operands
/// (path-prefix reads, peer binders) skip.
/// </summary>
internal sealed class DateOperationTypeCheck : IExpressionTypeCheck {
    public Type ExpressionType => typeof(DateOperation);

    public void Check(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope) {
        if (expression is not DateOperation dateOp)
            return;

        var dateType = InferTypeOf(dateOp.Date, scope);
        if (dateType is not (ExpressionTypeAnalyzer.TypeCategory.Unknown or ExpressionTypeAnalyzer.TypeCategory.Date)) {
            context.ReportError(dateOp,
                $"temporal offset requires a date left operand (got '{DescribeCategory(dateType)}'); " +
                "a duration needs a date or clock node ('Now'/'Today') to offset",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static string DescribeCategory(ExpressionTypeAnalyzer.TypeCategory c) => c switch {
        ExpressionTypeAnalyzer.TypeCategory.Text => "Text",
        ExpressionTypeAnalyzer.TypeCategory.Number => "Number",
        ExpressionTypeAnalyzer.TypeCategory.Boolean => "Boolean",
        ExpressionTypeAnalyzer.TypeCategory.Date => "Date",
        ExpressionTypeAnalyzer.TypeCategory.Duration => "duration",
        ExpressionTypeAnalyzer.TypeCategory.Enum => "enum",
        ExpressionTypeAnalyzer.TypeCategory.Guid => "Guid",
        ExpressionTypeAnalyzer.TypeCategory.Null => "null",
        _ => "unknown",
    };

    private static ExpressionTypeAnalyzer.TypeCategory InferTypeOf(DomainExpression expr, ExpressionTypeCheckScope scope) {
        if (expr is PropertyAccess pa && ResolvePropertyType(pa.Name, scope) is { } pt)
            return CategoryOfTypeName(pt, scope.EnumTypes);
        if (expr is ParameterAccess param && scope.Parameters?.TryGetValue(param.Name, out var pname) == true)
            return CategoryOfTypeName(pname, scope.EnumTypes);
        return CategoryOfExpression(expr);
    }

    private static string? ResolvePropertyType(string name, ExpressionTypeCheckScope scope) {
        if (scope.Properties.TryGetValue(name, out var pt)) return pt;
        if (scope.Parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    private static ExpressionTypeAnalyzer.TypeCategory CategoryOfTypeName(string typeName, IReadOnlyDictionary<string, EnumType> enumTypes) {
        if (enumTypes.ContainsKey(typeName)) return ExpressionTypeAnalyzer.TypeCategory.Enum;
        return typeName switch {
            "Text" or "String" => ExpressionTypeAnalyzer.TypeCategory.Text,
            "Number" or "Int" or "Int64" or "Int32" or "Decimal" or "Float" or "Double" => ExpressionTypeAnalyzer.TypeCategory.Number,
            "Boolean" or "Bool" => ExpressionTypeAnalyzer.TypeCategory.Boolean,
            "DateTime" or "Timestamp" or "Date" or "DateOnly" => ExpressionTypeAnalyzer.TypeCategory.Date,
            "Time" or "TimeOnly" or "Duration" or "TimeSpan" => ExpressionTypeAnalyzer.TypeCategory.Duration,
            "Uuid" or "Guid" => ExpressionTypeAnalyzer.TypeCategory.Guid,
            _ => ExpressionTypeAnalyzer.TypeCategory.Unknown,
        };
    }

    private static ExpressionTypeAnalyzer.TypeCategory CategoryOfExpression(DomainExpression expr) => expr switch {
        Literal { Value: null } => ExpressionTypeAnalyzer.TypeCategory.Null,
        Literal { Value: string } => ExpressionTypeAnalyzer.TypeCategory.Text,
        Literal { Value: bool } => ExpressionTypeAnalyzer.TypeCategory.Boolean,
        Literal { Value: long or int or double or float or decimal or short or byte } => ExpressionTypeAnalyzer.TypeCategory.Number,
        DateOperation or Now or Today => ExpressionTypeAnalyzer.TypeCategory.Date,
        Duration => ExpressionTypeAnalyzer.TypeCategory.Duration,
        _ => ExpressionTypeAnalyzer.TypeCategory.Unknown,
    };
}

/// <summary>
/// Check: <c>default(3 days)</c> is a bare duration with no temporal left operand — reject.
/// </summary>
internal sealed class DurationDefaultCheck : IExpressionTypeCheck {
    public Type ExpressionType => typeof(Duration);

    public void Check(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope) {
        if (expression is not Duration d)
            return;
        context.ReportError(expression,
            $"default value '{d.Amount} {d.Unit}' is a bare duration with no temporal left operand",
            DomainModelDiagnosticCodes.SemanticTypeCompatibility);
    }
}

/// <summary>
/// Check: <c>default(Now)</c> is valid only on a Date-typed property; anything else rejected.
/// </summary>
internal sealed class NowDefaultCheck : IExpressionTypeCheck {
    public Type ExpressionType => typeof(Now);

    public void Check(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope) {
        if (expression is not Now)
            return;
        ClockDefaultChecks.CheckClockDefault(context, expression, scope, "Now");
    }
}

/// <summary>
/// Check: <c>default(today)</c> is valid only on a Date-typed property; anything else rejected.
/// </summary>
internal sealed class TodayDefaultCheck : IExpressionTypeCheck {
    public Type ExpressionType => typeof(Today);

    public void Check(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope) {
        if (expression is not Today)
            return;
        ClockDefaultChecks.CheckClockDefault(context, expression, scope, "Today");
    }
}

internal static class ClockDefaultChecks {
    /// <summary>
    /// Shared rule: <c>default(Now)</c>/<c>default(today)</c> require a Date/DateTime target.
    /// </summary>
    public static void CheckClockDefault(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope,
        string spelling) {
        var target = scope.DefaultTargetTypeName;
        if (target is null)
            return;
        var category = target switch {
            "DateTime" or "Timestamp" or "Date" or "DateOnly" => ExpressionTypeAnalyzer.TypeCategory.Date,
            _ => ExpressionTypeAnalyzer.TypeCategory.Unknown,
        };
        if (category is not ExpressionTypeAnalyzer.TypeCategory.Date) {
            context.ReportError(expression,
                $"default({spelling}) is not compatible with property type '{target}' " +
                "(use a date property, or 'Guid' for identifiers)",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }
}