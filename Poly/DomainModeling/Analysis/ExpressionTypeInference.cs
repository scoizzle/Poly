using Poly.DomainModeling.Ontology;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

// Type inference: what an expression's type is. Moved out of the core file unchanged.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>
    /// <see cref="InferType"/> plus the arithmetic result types it leaves Unknown: numeric
    /// arithmetic is Number, and Text + Text is Text. Used for call arguments and function bodies.
    /// </summary>
    private TypeInfo InferValueType(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        if (expr is Add or Subtract or Multiply or Divide) {
            var operands = expr.Children.OfType<DomainExpression>()
                .Select(c => InferValueType(context, c, props, parameters, enumTypes)).ToList();
            if (operands.All(o => IsNumeric(o.Category)))
                return new(TypeCategory.Number);
            if (expr is Add && operands.All(o => o.Category is TypeCategory.Text))
                return new(TypeCategory.Text);
            return new(TypeCategory.Unknown);
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    /// <summary>Infers the type of a binder-rooted argument expression: a binder-root property
    /// access, or arithmetic (numeric/numeric, date/number) over it. Non-scalar binder roots
    /// (nested path-prefix) resolve to Unknown.</summary>
    private static TypeInfo InferBinderExpressionType(
        AnalysisContext context, string? targetEntityName, string binderName,
        DomainExpression expr, Dictionary<string, EnumType> enumTypes) {
        switch (expr) {
            case RelationshipNavigation rn when string.Equals(rn.RelationshipName, binderName, StringComparison.Ordinal):
                if (rn.TargetProperty is not PropertyAccess pa) return new(TypeCategory.Unknown);
                var propType = ResolveTargetPropType(context, targetEntityName, pa.Name);
                return propType is null ? new(TypeCategory.Unknown) : new(CategoryOf(context, propType, enumTypes), propType);
            case Add or Subtract or Multiply or Divide:
                var operandTypes = expr.Children.OfType<DomainExpression>()
                    .Select(c => InferBinderExpressionType(context, targetEntityName, binderName, c, enumTypes))
                    .ToList();
                if (operandTypes.Any(t => t.Category is TypeCategory.Unknown)) return new(TypeCategory.Unknown);
                if (operandTypes.All(t => IsNumeric(t.Category))) return new(TypeCategory.Number);
                if (operandTypes.Count == 2 && IsDate(operandTypes[0].Category) && IsNumeric(operandTypes[1].Category))
                    return operandTypes[0];
                // Mixed non-numeric binder prop + number (line Status + 1): surface the
                // non-numeric type so the param-type mismatch fires instead of passing.
                return operandTypes.FirstOrDefault(t => !IsNumeric(t.Category));
            case Literal { Value: long or int or double or float or decimal or short or byte }:
                return new(TypeCategory.Number);
            case Literal { Value: string }:
                return new(TypeCategory.Text);
            default:
                return new(TypeCategory.Unknown);
        }
    }

    private static string? ResolveTargetPropType(AnalysisContext context, string? targetEntityName, string propName) {
        if (targetEntityName is null) return null;
        var lookup = context.GetTypeLookup();
        var entity = lookup?.Domain?.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
        return entity?.Properties.FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.Ordinal))?.Type.TypeName;
    }

    private TypeInfo InferLiteralAware(
        AnalysisContext context,
        DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes,
        Dictionary<string, string>? props = null, Dictionary<string, string>? parameters = null) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        if (expr is PropertyAccess pa && CategoryOf(context, targetTypeName, enumTypes) is TypeCategory.Enum) {
            if (enumTypes.TryGetValue(targetTypeName, out var enumType)
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
                return new(TypeCategory.Enum, targetTypeName);
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    private TypeInfo InferType(
        AnalysisContext context,
        DomainExpression expr,
        Dictionary<string, string>? props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        expr = RewriteLibraryIdent(expr);
        if (context.GetMetadata<CatalogTypedExpressionMetadata>(expr) is { TypeName: { } stamped })
            return new(CategoryOf(context, stamped, enumTypes), stamped);

        if (_meaning.Inference.TryDispatch(
                expr,
                child => InferType(context, child, props, parameters, enumTypes).TypeName ?? "",
                out var inferredName)
            && !string.IsNullOrEmpty(inferredName)) {
            context.SetMetadata(expr, new CatalogTypedExpressionMetadata(inferredName));
            return new(CategoryOf(context, inferredName, enumTypes), inferredName);
        }

        // Text + Text is concatenation and yields Text; every other arithmetic result stays Unknown here.
        if (expr is Add add
            && InferType(context, add.Left, props, parameters, enumTypes).Category is TypeCategory.Text
            && InferType(context, add.Right, props, parameters, enumTypes).Category is TypeCategory.Text)
            return new(TypeCategory.Text);

        return expr switch {
            PropertyAccess pa => ResolvePropertyType(pa.Name, props, parameters) is { } pt
                ? new(CategoryOf(context, pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            ParameterAccess pa => parameters?.TryGetValue(pa.Name, out var pt) == true
                ? new(CategoryOf(context, pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            Literal { Value: null } => new(TypeCategory.Null),
            Literal { Value: string } => new(TypeCategory.Text),
            Literal { Value: bool } => new(TypeCategory.Boolean),
            Literal { Value: decimal } => new(TypeCategory.Number, "Decimal"),
            Literal { Value: long or int or double or float or short or byte } => new(TypeCategory.Number),
            Exists or NotExists or AnyExpr or AllExpr or NoneExpr or Comparison or And or Or or Not => new(TypeCategory.Boolean),
            CountExpr => new(TypeCategory.Number),
            SumExpr => new(TypeCategory.Number),
            MatchExpr match => InferMatch(context, match, props, parameters, enumTypes),
            FunctionCall call => FunctionAnalysis.Find(context, call.Name) is { } function
                ? new(CategoryOf(context, function.ReturnType.TypeName, enumTypes), function.ReturnType.TypeName)
                : new(TypeCategory.Unknown),
            _ => new(TypeCategory.Unknown),
        };
    }

    private static string? ResolvePropertyType(string name, Dictionary<string, string>? props, Dictionary<string, string>? parameters) {
        if (props?.TryGetValue(name, out var pt) == true) return pt;
        if (parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    private static TypeCategory CategoryOf(
        AnalysisContext context, string typeName, Dictionary<string, EnumType> enumTypes) {
        if (enumTypes.ContainsKey(typeName)) return TypeCategory.Enum;
        var lookup = context.GetTypeLookup();
        if (lookup?.Domain.Types.OfType<Ontology.PrimitiveType>().FirstOrDefault(p =>
                string.Equals(p.Name, typeName, StringComparison.Ordinal)) is { } primitive)
            return FromSemantics(primitive.Semantics);
        return typeName switch {
            "Text" or "String" => TypeCategory.Text,
            "Number" or "Int" or "Int64" or "Int32" or "Decimal" or "Float" or "Double" => TypeCategory.Number,
            "Boolean" or "Bool" => TypeCategory.Boolean,
            "Uuid" or "Guid" => TypeCategory.Guid,
            _ => TypeCategory.Unknown,
        };
    }

    private static TypeCategory FromSemantics(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => TypeCategory.Boolean,
        PrimitiveSemantics.Number => TypeCategory.Number,
        PrimitiveSemantics.Decimal => TypeCategory.Number,
        PrimitiveSemantics.Text => TypeCategory.Text,
        PrimitiveSemantics.Uuid => TypeCategory.Guid,
        PrimitiveSemantics.Binary => TypeCategory.Unknown,
        PrimitiveSemantics.Date => TypeCategory.Date,
        // DateTime keeps the Date category; granularity is compared through SemanticsOf.
        PrimitiveSemantics.DateTime => TypeCategory.Date,
        PrimitiveSemantics.Time => TypeCategory.Time,
        PrimitiveSemantics.Duration => TypeCategory.Duration,
        _ => throw new InvalidOperationException($"Unmapped primitive semantics '{semantics}'."),
    };
}