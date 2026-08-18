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
/// Core expression print binders registered in every <see cref="DomainDslPrinter"/>
/// print session. pack-1-3: expr-primary identifiers/literals/true/false/null print
/// through the Grammar <see cref="Printer{TToken,TTokenKind}"/> table — fills supply
/// the identifier/literal text and <see cref="DslTokenWriter"/> owns separators.
///
/// Constructs without a printable table pattern yet (logical/arithmetic/comparison
/// operators and the relationship forms) register a fallback binder: the owner type
/// is claimed (fail-closed against duplicate pack binders) while printing defers to
/// the existing v1 dispatch until a pattern exists.
/// </summary>
public static class CoreExpressionPrintBinders {
    /// <summary>Registers the core binders on <paramref name="registry"/>.</summary>
    public static void Register(ExpressionPrintRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new PrimaryBinding(typeof(PropertyAccess), "expr-primary", "ident"));
        registry.Register(new PrimaryBinding(typeof(ParameterAccess), "expr-primary", "ident"));
        registry.Register(new LiteralBinding());

        registry.Register(new FallbackBinding(typeof(And)));
        registry.Register(new FallbackBinding(typeof(Or)));
        registry.Register(new FallbackBinding(typeof(Not)));
        registry.Register(new FallbackBinding(typeof(Add)));
        registry.Register(new FallbackBinding(typeof(Subtract)));
        registry.Register(new FallbackBinding(typeof(Multiply)));
        registry.Register(new FallbackBinding(typeof(Divide)));
        registry.Register(new FallbackBinding(typeof(Comparison)));
        registry.Register(new FallbackBinding(typeof(OwnedAccess)));
        registry.Register(new FallbackBinding(typeof(RelationshipNavigation)));
        registry.Register(new FallbackBinding(typeof(Exists)));
        registry.Register(new FallbackBinding(typeof(NotExists)));
        registry.Register(new FallbackBinding(typeof(AnyExpr)));
        registry.Register(new FallbackBinding(typeof(AllExpr)));
        registry.Register(new FallbackBinding(typeof(NoneExpr)));
        registry.Register(new FallbackBinding(typeof(CountExpr)));
    }

    /// <summary>Binds one concrete expression type to a fixed (rule, pattern).</summary>
    private sealed class PrimaryBinding : IExpressionPrintMapping {
        private readonly Type _type;
        private readonly PrintMapping _mapping;

        public PrimaryBinding(Type type, string rule, string pattern) {
            _type = type;
            _mapping = new PrintMapping(rule, pattern);
        }

        public Type ExpressionType => _type;

        public bool TryMap(DomainExpression expression, out PrintMapping mapping) {
            if (expression.GetType() == _type) {
                mapping = _mapping;
                return true;
            }
            mapping = default;
            return false;
        }
    }

    /// <summary>Maps a <see cref="Literal"/> to the matching expr-primary pattern by value shape.</summary>
    private sealed class LiteralBinding : IExpressionPrintMapping {
        public Type ExpressionType => typeof(Literal);

        public bool TryMap(DomainExpression expression, out PrintMapping mapping) {
            if (expression is not Literal literal) {
                mapping = default;
                return false;
            }

            mapping = literal.Value switch {
                null => new PrintMapping("expr-primary", "null"),
                true => new PrintMapping("expr-primary", "true"),
                false => new PrintMapping("expr-primary", "false"),
                string => new PrintMapping("expr-primary", "string"),
                long or double => new PrintMapping("expr-primary", "number"),
                _ => default,
            };
            return mapping.Rule is not null;
        }
    }

    /// <summary>
    /// Claims an expression owner while a printable table pattern is pending.
    /// Printing defers to the existing dispatch (pack-1-3 temporary fallback).
    /// </summary>
    private sealed class FallbackBinding : IExpressionPrintMapping {
        private readonly Type _type;

        public FallbackBinding(Type type) => _type = type;

        public Type ExpressionType => _type;

        public bool TryMap(DomainExpression expression, out PrintMapping mapping) {
            mapping = default;
            return false;
        }
    }
}