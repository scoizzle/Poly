using Poly.Ast.Nodes;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using Prim = Poly.Introspection.PrimitiveType;
using SN = Poly.Ast.Nodes;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>Syntax lowering for temporal IR. Registered on session Meaning; core does not name these types.</summary>
internal static class TemporalLowering {
    public static void Register(ExpressionMeaning meaning) {
        ArgumentNullException.ThrowIfNull(meaning);
        meaning.Lowering.Register(new NowLowering());
        meaning.Lowering.Register(new TodayLowering());
        meaning.Lowering.Register(new DateOperationLowering());
        meaning.Lowering.Register(new DateAddDaysLowering(typeof(Add), subtract: false));
        meaning.Lowering.Register(new DateAddDaysLowering(typeof(Subtract), subtract: true));
        meaning.Defaults.Register(new NowDefaultResolver());
        meaning.Defaults.Register(new TodayDefaultResolver());
        meaning.Defaults.Register(new DurationDefaultResolver());
    }

    private sealed class NowLowering : IExpressionLoweringHandler {
        public Type ExpressionType => typeof(Now);

        public bool TryLower(
            DomainExpression expression,
            Func<DomainExpression, Node> route,
            Func<string, string?>? propertyTypeResolver,
            out Node result) {
            result = UtcNowMember();
            return true;
        }
    }

    private sealed class TodayLowering : IExpressionLoweringHandler {
        public Type ExpressionType => typeof(Today);

        public bool TryLower(
            DomainExpression expression,
            Func<DomainExpression, Node> route,
            Func<string, string?>? propertyTypeResolver,
            out Node result) {
            result = DateOnlyFromUtcNow();
            return true;
        }
    }

    private sealed class DateOperationLowering : IExpressionLoweringHandler {
        public Type ExpressionType => typeof(DateOperation);

        public bool TryLower(
            DomainExpression expression,
            Func<DomainExpression, Node> route,
            Func<string, string?>? propertyTypeResolver,
            out Node result) {
            var d = (DateOperation)expression;
            var date = route(d.Date);
            var offset = route(d.Offset);
            var family = ResolveDateClrFamily(d.Date, propertyTypeResolver);

            if (family is DateClrFamily.TimeOnly
                && d.Kind is DateOperationKind.AddSeconds or DateOperationKind.AddMilliseconds) {
                var from = d.Kind is DateOperationKind.AddSeconds ? "FromSeconds" : "FromMilliseconds";
                result = new Invoke(
                    new Member(date, "Add"),
                    new Invoke(new Member(new NamedTypeReference("TimeSpan"), from), offset));
                return true;
            }

            var (method, arg) = d.Kind switch {
                DateOperationKind.AddMilliseconds => ("AddMilliseconds", offset),
                DateOperationKind.AddSeconds => ("AddSeconds", offset),
                DateOperationKind.AddMinutes => ("AddMinutes", offset),
                DateOperationKind.AddHours => ("AddHours", offset),
                DateOperationKind.AddDays => ("AddDays", offset),
                DateOperationKind.AddWeeks => ("AddDays", ScaleOffset(offset, 7)),
                DateOperationKind.AddMonths => ("AddMonths", offset),
                DateOperationKind.AddYears => ("AddYears", offset),
                DateOperationKind.DiffDays => ("Subtract", offset),
                _ => throw new NotSupportedException($"DateOperation kind '{d.Kind}' is not supported."),
            };

            if (NeedsIntOffset(family, d.Kind))
                arg = Int32Offset(arg);

            result = new Invoke(new Member(date, method), arg);
            return true;
        }
    }

    /// <summary>
    /// <c>DueDate + 14</c> / <c>DueDate - 7</c> → AddDays. Claims only date-typed
    /// property left operands; other Add/Subtract stay core arithmetic.
    /// </summary>
    private sealed class DateAddDaysLowering : IExpressionLoweringHandler {
        public Type ExpressionType { get; }
        private readonly bool _subtract;

        public DateAddDaysLowering(Type expressionType, bool subtract) {
            ExpressionType = expressionType;
            _subtract = subtract;
        }

        public bool TryLower(
            DomainExpression expression,
            Func<DomainExpression, Node> route,
            Func<string, string?>? propertyTypeResolver,
            out Node result) {
            result = null!;
            DomainExpression leftExpr;
            DomainExpression rightExpr;
            if (_subtract) {
                var s = (Subtract)expression;
                leftExpr = s.Left;
                rightExpr = s.Right;
            }
            else {
                var a = (Add)expression;
                leftExpr = a.Left;
                rightExpr = a.Right;
            }
            if (leftExpr is not PropertyAccess pa
                || propertyTypeResolver?.Invoke(pa.Name) is not { } typeName)
                return false;
            var family = FamilyFromTypeName(typeName);
            if (family is not DateClrFamily.DateOnly and not DateClrFamily.DateTime)
                return false;
            if (_subtract
                && rightExpr is PropertyAccess rightPa
                && propertyTypeResolver?.Invoke(rightPa.Name) is { } rightTypeName
                && FamilyFromTypeName(rightTypeName) == family) {
                result = new Invoke(new Member(route(leftExpr), "Subtract"), [route(rightExpr)]);
                return true;
            }
            var left = route(leftExpr);
            var right = route(rightExpr);
            var rawArg = _subtract ? (Node)new SN.UnaryMinus(right) : right;
            var typedArg = family is DateClrFamily.DateOnly ? Int32Offset(rawArg) : rawArg;
            result = new Invoke(new Member(left, "AddDays"), [typedArg]);
            return true;
        }
    }

    private sealed class NowDefaultResolver : IExpressionDefaultResolver {
        public Type ExpressionType => typeof(Now);

        public bool TryResolve(
            DomainExpression expression,
            string? propTypeName,
            out object? runtimeValue,
            out Node exportNode) {
            var dateTime = propTypeName is "DateTime" or "Timestamp";
            runtimeValue = dateTime ? DateTime.UtcNow : DateOnly.FromDateTime(DateTime.UtcNow);
            exportNode = dateTime ? UtcNowMember() : DateOnlyFromUtcNow();
            return true;
        }
    }

    private sealed class TodayDefaultResolver : IExpressionDefaultResolver {
        public Type ExpressionType => typeof(Today);

        public bool TryResolve(
            DomainExpression expression,
            string? propTypeName,
            out object? runtimeValue,
            out Node exportNode) {
            var dateTime = propTypeName is "DateTime" or "Timestamp";
            runtimeValue = dateTime ? DateTime.UtcNow : DateOnly.FromDateTime(DateTime.UtcNow);
            exportNode = dateTime ? UtcNowMember() : DateOnlyFromUtcNow();
            return true;
        }
    }

    /// <summary>
    /// A bare <c>Duration</c> default on a <c>Duration</c> property
    /// (<c>Budget: Duration default(30 Minutes)</c>) lowers to its
    /// <c>TimeSpan</c> construction. Without this the constant is not a literal or
    /// an enum member, so the exporter would drop it and the create factory would
    /// lose its default. Months and years have no fixed <c>TimeSpan</c> length and
    /// fail closed instead of approximating a calendar span.
    /// </summary>
    private sealed class DurationDefaultResolver : IExpressionDefaultResolver {
        public Type ExpressionType => typeof(Duration);

        public bool TryResolve(
            DomainExpression expression,
            string? propTypeName,
            out object? runtimeValue,
            out Node exportNode) {
            runtimeValue = null;
            exportNode = null!;
            if (expression is not Duration duration)
                return false;
            if (propTypeName is not ("Duration" or "TimeSpan"))
                return false;
            runtimeValue = ToTimeSpan(duration);
            exportNode = ToTimeSpanNode(duration);
            return true;
        }
    }

    private static TimeSpan ToTimeSpan(Duration d) => d.Unit switch {
        DurationUnit.Milliseconds => TimeSpan.FromMilliseconds(d.Amount),
        DurationUnit.Seconds => TimeSpan.FromSeconds(d.Amount),
        DurationUnit.Minutes => TimeSpan.FromMinutes(d.Amount),
        DurationUnit.Hours => TimeSpan.FromHours(d.Amount),
        DurationUnit.Days => TimeSpan.FromDays(d.Amount),
        DurationUnit.Weeks => TimeSpan.FromDays(WeeksAsDays(d.Amount, d.Unit)),
        _ => throw UnsupportedDurationDefault(d),
    };

    private static Node ToTimeSpanNode(Duration d) {
        var (factory, amount) = d.Unit switch {
            DurationUnit.Milliseconds => ("FromMilliseconds", d.Amount),
            DurationUnit.Seconds => ("FromSeconds", d.Amount),
            DurationUnit.Minutes => ("FromMinutes", d.Amount),
            DurationUnit.Hours => ("FromHours", d.Amount),
            DurationUnit.Days => ("FromDays", d.Amount),
            DurationUnit.Weeks => ("FromDays", WeeksAsDays(d.Amount, d.Unit)),
            _ => throw UnsupportedDurationDefault(d),
        };
        return new Invoke(
            new Member(new NamedTypeReference("TimeSpan"), factory),
            new Constant(amount));
    }

    private static long WeeksAsDays(long weeks, DurationUnit unit) {
        try {
            return checked(weeks * 7);
        }
        catch (OverflowException) {
            throw UnsupportedDurationDefault(new Duration(weeks, unit));
        }
    }

    private static NotSupportedException UnsupportedDurationDefault(Duration d) =>
        new($"default({d.Amount} {d.Unit}) cannot be lowered to a TimeSpan default: " +
            "months and years have no fixed length.");

    private enum DateClrFamily { Unknown, DateOnly, DateTime, TimeOnly }

    private static DateClrFamily ResolveDateClrFamily(
        DomainExpression dateExpr,
        Func<string, string?>? propertyTypeResolver) {
        if (dateExpr is Now)
            return DateClrFamily.DateTime;
        if (dateExpr is Today)
            return DateClrFamily.DateOnly;
        if (DateOperandName(dateExpr) is { } name
            && propertyTypeResolver?.Invoke(name) is { } typeName)
            return FamilyFromTypeName(typeName);
        if (dateExpr is DateOperation nested)
            return ResolveDateClrFamily(nested.Date, propertyTypeResolver);
        return DateClrFamily.Unknown;
    }

    private static string? DateOperandName(DomainExpression dateExpr) => dateExpr switch {
        PropertyAccess pa => pa.Name,
        ParameterAccess pa => pa.Name,
        _ => null,
    };

    private static DateClrFamily FamilyFromTypeName(string typeName) => typeName switch {
        "Date" or "DateOnly" => DateClrFamily.DateOnly,
        "DateTime" or "Timestamp" => DateClrFamily.DateTime,
        "Time" or "TimeOnly" => DateClrFamily.TimeOnly,
        _ => DateClrFamily.Unknown,
    };

    private static bool NeedsIntOffset(DateClrFamily family, DateOperationKind kind) {
        if (kind is DateOperationKind.AddMonths or DateOperationKind.AddYears)
            return family is not DateClrFamily.TimeOnly;
        return family is DateClrFamily.DateOnly
            && kind is DateOperationKind.AddDays or DateOperationKind.AddWeeks;
    }

    private static Node Int32Offset(Node rawArg) =>
        new TypeCast(rawArg, new PrimitiveTypeReference(Prim.Int32));

    private static Node ScaleOffset(Node offset, int factor) => offset switch {
        Constant { Value: long n } => new Constant(n * factor),
        Constant { Value: int n } => new Constant(n * factor),
        _ => new SN.Multiply(offset, new Constant((long)factor)),
    };

    private static Node UtcNowMember() =>
        new Member(new NamedTypeReference("DateTime"), "UtcNow");

    private static Node DateOnlyFromUtcNow() =>
        new Invoke(
            new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
            UtcNowMember());
}