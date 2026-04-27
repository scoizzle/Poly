using Poly.Data.Modeling;
using Poly.Syntax.AbstractSyntaxTree;
using Poly.Syntax.Analysis;
using Poly.Tests.TestHelpers;

using And = Poly.Syntax.AbstractSyntaxTree.And;
using Equal = Poly.Syntax.AbstractSyntaxTree.Equal;
using GreaterThanOrEqual = Poly.Syntax.AbstractSyntaxTree.GreaterThanOrEqual;
using LessThanOrEqual = Poly.Syntax.AbstractSyntaxTree.LessThanOrEqual;

namespace Poly.Tests.Data.Modeling;

public class DomainLoweringGeneratorTests {
    private sealed class Person {
        public int Age { get; init; }
    }

    [Test]
    public async Task Lower_EqualMemberLiteral_CompilesAndEvaluates() {
        var subject = new Parameter("@value", new TypeReference(typeof(Person).FullName!));
        var clause = new Equal(new Member(subject, "Age"), new Constant(18));

        var analysis = new AnalysisResult(new NodeMetadataStore());
        var generator = new DomainLoweringGenerator(analysis);

        var lowered = generator.Lower(clause, subject);
        var predicate = lowered.CompileLambda<Func<Person, bool>>((subject, typeof(Person)));

        await Assert.That(predicate(new Person { Age = 18 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 19 })).IsFalse();
    }

    [Test]
    public async Task Lower_RangeExpression_CompilesAndEvaluates() {
        var subject = new Parameter("@value", new TypeReference(typeof(Person).FullName!));
        var value = new Member(subject, "Age");
        var clause = new And(
            new GreaterThanOrEqual(value, new Constant(18)),
            new LessThanOrEqual(value, new Constant(65)));

        var analysis = new AnalysisResult(new NodeMetadataStore());
        var generator = new DomainLoweringGenerator(analysis);

        var lowered = generator.Lower(clause, subject);
        var predicate = lowered.CompileLambda<Func<Person, bool>>((subject, typeof(Person)));

        await Assert.That(predicate(new Person { Age = 18 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 40 })).IsTrue();
        await Assert.That(predicate(new Person { Age = 17 })).IsFalse();
        await Assert.That(predicate(new Person { Age = 70 })).IsFalse();
    }
}