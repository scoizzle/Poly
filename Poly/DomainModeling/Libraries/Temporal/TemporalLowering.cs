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
            runtimeValue = dateTime ? DateTime.Today : DateOnly.FromDateTime(DateTime.Today);
            exportNode = dateTime
                ? new Member(new NamedTypeReference("DateTime"), "Today")
                : new Invoke(
                    new Member(new NamedTypeReference("DateOnly"), "FromDateTime"),
                    new Member(new NamedTypeReference("DateTime"), "Today"));
            return true;
        }
    }

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