using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;
using Poly.Grammar;

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

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// IR → Grammar (rule, pattern, fills). <paramref name="Fill"/> supplies text for
/// content positions; when null the printer uses its built-in per-type fill.
/// </summary>
public readonly record struct PrintMapping(
    string Rule,
    string Pattern,
    System.Action<PrintContext<DslToken, DslTokenKind>>? Fill = null,
    IReadOnlyDictionary<string, string>? NamedFills = null);

/// <summary>
/// Maps one concrete <see cref="DomainExpression"/> subtype to a print form.
/// Duplicate owners fail closed.
/// </summary>
public interface IExpressionPrintMapping {
    Type ExpressionType { get; }

    bool TryMap(DomainExpression expression, out PrintMapping mapping);
}

/// <summary>
/// Registry of expression print mappings. First match wins; a second owner for
/// the same type fails closed.
/// </summary>
public sealed class ExpressionPrintRegistry {
    private readonly List<IExpressionPrintMapping> _mappings = [];

    public void Register(IExpressionPrintMapping mapping) {
        ArgumentNullException.ThrowIfNull(mapping);
        if (_mappings.Any(existing => existing.ExpressionType == mapping.ExpressionType))
            throw new InvalidOperationException(
                $"An expression print mapping for '{mapping.ExpressionType.Name}' is already registered.");
        _mappings.Add(mapping);
    }

    public bool TryMap(DomainExpression expression, out PrintMapping mapping) {
        ArgumentNullException.ThrowIfNull(expression);
        foreach (var candidate in _mappings) {
            if (candidate.TryMap(expression, out mapping!))
                return true;
        }
        mapping = default;
        return false;
    }
}