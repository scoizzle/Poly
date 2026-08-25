using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Parsing;

public sealed class ExpressionPrintBinderTests {
    private sealed class TrueLiteralBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(Literal);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is Literal { Value: true }) {
                binding = new PrintMapping("expr-primary", "true");
                return true;
            }
            binding = default;
            return false;
        }
    }

    private sealed class ParameterBinder : IExpressionPrintMapping {
        public Type ExpressionType => typeof(ParameterAccess);

        public bool TryMap(DomainExpression expression, out PrintMapping binding) {
            if (expression is ParameterAccess parameter) {
                binding = new PrintMapping("expr-primary", parameter.Name);
                return true;
            }
            binding = default;
            return false;
        }
    }

    [Test]
    public async Task Register_DuplicateOwner_Throws() {
        var registry = new ExpressionPrintRegistry();
        registry.Register(new TrueLiteralBinder());

        await Assert.That(() => registry.Register(new TrueLiteralBinder()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TryBind_UnknownExpression_ReturnsFalse() {
        var registry = new ExpressionPrintRegistry();

        var found = registry.TryMap(DomainExpression.Property("Age"), out var binding);

        await Assert.That(found).IsFalse();
        await Assert.That(binding).IsEqualTo(default(PrintMapping));
    }

    [Test]
    public async Task TryBind_Registered_ReturnsRuleAndPattern() {
        var registry = new ExpressionPrintRegistry();
        registry.Register(new TrueLiteralBinder());
        registry.Register(new ParameterBinder());

        var foundLiteral = registry.TryMap(DomainExpression.Literal(true), out var literalBinding);
        var foundParameter = registry.TryMap(DomainExpression.Parameter("amount"), out var parameterBinding);

        await Assert.That(foundLiteral).IsTrue();
        await Assert.That(literalBinding).IsEqualTo(new PrintMapping("expr-primary", "true"));
        await Assert.That(foundParameter).IsTrue();
        await Assert.That(parameterBinding).IsEqualTo(new PrintMapping("expr-primary", "amount"));
    }
}