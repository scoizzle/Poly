using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// The ledger is a type system of its own (#998 T2; docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md
/// §4): it resolves its definitions, binds no words, decides its own operator, and answers a rule only it owns.
/// </summary>
public class LedgerTypeSystemTests {
    [Test]
    public async Task Ledger_ResolvesItsOwnDefinitions_AndBindsNoWords() {
        var host = new LedgerHost();
        ITypeSystem ledger = host;

        await Assert.That(ledger.Target).IsNull().Because("the ledger is a base system");
        await Assert.That(ledger.Sources).Contains((ITypeSource)host);
        await Assert.That(ledger.Resolve("ledger.amount")).IsSameReferenceAs(host.Amount);
        await Assert.That(ledger.Resolve("ledger.nothing")).IsNull();
        await Assert.That(ledger.Resolve("System.Decimal")).IsNull().Because("the ledger prescribes no CLR type");
        await Assert.That(ledger.Bind("decimal")).IsNull().Because("C#'s words are C#'s");
        await Assert.That(ledger.Spell(host.Amount)).IsNull();
        await Assert.That(ledger.IsSame(host.Amount, host.Amount)).IsTrue();
        await Assert.That(ledger.IsSame(host.Amount, host.Account)).IsFalse();
    }

    [Test]
    public async Task Ledger_DecidesItsOwnOperator_AndNoOther() {
        var host = new LedgerHost();
        ITypeSystem ledger = host;
        ITypeDefinition clrInt = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(int));

        var add = ledger.Operator(OperatorKind.Add, host.Amount, host.Amount);

        await Assert.That(add).IsNotNull();
        await Assert.That(add!.Kind).IsEqualTo(OperatorKind.Add);
        await Assert.That(add.ResultTypeDefinition).IsSameReferenceAs(host.Amount);
        await Assert.That(add.DeclaringTypeDefinition).IsSameReferenceAs(host.Amount);
        await Assert.That(add.Method).IsNull().Because("the system applies it; no member implements it");
        await Assert.That(add.Parameters.Select(static parameter => parameter.ParameterTypeDefinition))
            .IsEquivalentTo(new[] { host.Amount, host.Amount });
        await Assert.That(ledger.Operator(OperatorKind.Subtract, host.Amount, host.Amount)).IsNull();
        await Assert.That(ledger.Operator(OperatorKind.Add, host.Account, host.Account)).IsNull();
        await Assert.That(ledger.Operator(OperatorKind.Add, host.Amount, clrInt)).IsNull()
            .Because("the ledger has no rule for a CLR operand: fail closed");
        await Assert.That(CSharpTypeSystem.Shared.Operator(OperatorKind.Add, clrInt, clrInt)).IsNotNull();
    }

    [Test]
    public async Task Ledger_AnswersItsOwnRule_AndTheCSharpSystemDoesNot() {
        var host = new LedgerHost();
        ITypeSystem ledger = host;

        var rules = ledger.Rule<LedgerHost.ILedgerRules>();

        await Assert.That(rules).IsNotNull();
        await Assert.That(rules!.MinorUnits(host.Amount)).IsEqualTo(2);
        await Assert.That(rules.MinorUnits(host.Entry)).IsNull();
        await Assert.That(rules.MinorUnits(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(decimal)))).IsNull()
            .Because("the ledger answers for its own definitions only");
        await Assert.That(CSharpTypeSystem.Shared.Rule<LedgerHost.ILedgerRules>()).IsNull();
    }

    [Test]
    public async Task TwoLedgers_EachAnswersOnlyForItsOwnDefinitions() {
        ITypeSystem first = new LedgerHost();
        var second = new LedgerHost();

        await Assert.That(first.Rule<LedgerHost.ILedgerRules>()!.MinorUnits(second.Amount)).IsNull()
            .Because("another system's amount is not this system's, though it is the same kind of definition");
        await Assert.That(first.IsSame(((LedgerHost)first).Amount, second.Amount)).IsFalse();
        await Assert.That(first.Operator(OperatorKind.Add, second.Amount, second.Amount)).IsNull();
    }
}