using System.Reflection;

using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// The type system (#998 T1; docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md): the
/// C# system binds and spells its keywords and answers the conversion and operator rules today's doors answer; rules
/// are typed questions declared outside Introspection on one base; the system's surface is decided, not grown.
/// </summary>
public class TypeSystemTests {
    private static readonly string[] Keywords = [
        "bool", "sbyte", "short", "int", "long", "byte", "ushort", "uint", "ulong", "float", "double", "decimal",
        "string", "char", "object",
    ];

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    public async Task CSharp_BindsEveryKeyword_ToTheClrDefinition_AndSpellsItBack() {
        var csharp = CSharpTypeSystem.Shared;
        foreach (var keyword in Keywords) {
            var bound = csharp.Bind(keyword);
            await Assert.That(bound).IsNotNull().Because($"'{keyword}' is a C# type keyword");
            await Assert.That(TypeDefinitionExtensions.IsSameType(bound!, Clr(CSharpTypeNames.TypeOf(keyword)!))).IsTrue();
            await Assert.That(csharp.Spell(bound!)).IsEqualTo(keyword);
        }
        await Assert.That(csharp.Bind("Int32")).IsNull().Because("a type's name is not a C# word; the scope binds names");
        await Assert.That(csharp.Bind("Number")).IsNull().Because("the domain's words are the domain's");
        await Assert.That(csharp.Spell(Clr(typeof(Uri)))).IsNull();
    }

    [Test]
    public async Task CSharp_Relations_AreTheDefinitionsAnswers() {
        var csharp = CSharpTypeSystem.Shared;
        await Assert.That(csharp.IsSame(Clr(typeof(int)), Clr(typeof(int)))).IsTrue();
        await Assert.That(csharp.IsSame(Clr(typeof(int)), Clr(typeof(long)))).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(csharp.NullableOf(Clr(typeof(int?)))!, Clr(typeof(int)))).IsTrue();
        await Assert.That(csharp.NullableOf(Clr(typeof(int)))).IsNull();
        await Assert.That(TypeDefinitionExtensions.IsSameType(csharp.ElementType(Clr(typeof(List<int>)))!, Clr(typeof(int)))).IsTrue();
        await Assert.That(csharp.IsAssignableWithoutConversion(Clr(typeof(object)), Clr(typeof(string)))).IsTrue();
        await Assert.That(csharp.IsAssignableWithoutConversion(Clr(typeof(string)), Clr(typeof(object)))).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(csharp.Resolve("System.Uri")!, Clr(typeof(Uri)))).IsTrue();
        await Assert.That(csharp.Sources).Contains((ITypeSource)ClrTypeDefinitionRegistry.Shared);
        await Assert.That(csharp.Target).IsNull().Because("C# over the CLR is a base system");
    }

    [Test]
    public async Task Rule_IsTheRegisteredOne_ElseTheSystemItself_ElseNone() {
        var csharp = new CSharpTypeSystem();
        await Assert.That(csharp.Rule<IProbeRule>()).IsNull().Because("nothing answers a question no one registered");

        var probe = new ProbeRule();
        csharp.Rules.Add<IProbeRule>(probe);

        await Assert.That(csharp.Rule<IProbeRule>()).IsSameReferenceAs(probe);
        await Assert.That(() => csharp.Rules.Add<IProbeRule>(new ProbeRule())).Throws<InvalidOperationException>()
            .Because("a second rule of the same type is an error, never a silent replacement");
        await Assert.That(CSharpTypeSystem.Shared.Rule<IProbeRule>()).IsNull().Because("rules belong to one system");
    }

    [Test]
    public async Task Rules_AnswerExactlyTheQuestionTheyWereRegisteredFor() {
        var rules = new TypeSystemRules();
        var both = new BothRules();

        rules.Add<IProbeRule>(both);

        await Assert.That(rules.Find<IProbeRule>()).IsSameReferenceAs(both);
        await Assert.That(rules.Find<IOtherProbeRule>()).IsNull()
            .Because("a rule answers the question it was registered for, not every interface its object implements");
        await Assert.That(() => rules.Add<IOtherProbeRule>(both)).ThrowsNothing();
        await Assert.That(rules.Find<IOtherProbeRule>()).IsSameReferenceAs(both);
    }

    // ── Guards: the type system's surface is decided, and rules sit on one base outside Introspection ──

    private static readonly string[] DecidedTypeSystemMembers = [
        "Target", "Sources", "Resolve", "Bind", "Spell", "IsSame", "NullableOf", "ElementType",
        "IsAssignableWithoutConversion", "Conversion", "Operator", "Rule",
    ];

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // A property's or an event's accessor is that member, not another; every other method counts, operators included.
    private static bool IsAccessor(MemberInfo member) =>
        member is MethodInfo { IsSpecialName: true } method
        && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    [Test]
    public async Task ITypeSystem_IsExactlyTheDecidedMembers() {
        var declared = typeof(ITypeSystem).GetMembers(Declared)
            .Where(static member => !IsAccessor(member))
            .Select(static member => member.Name);

        await Assert.That(declared).IsEquivalentTo(DecidedTypeSystemMembers)
            .Because("a member is added only by editing the decided list, citing the type-system decision");
    }

    [Test]
    public async Task ITypeSystem_ClassifiesNothing() => await Assert.That(Classifying(typeof(ITypeSystem))).IsEmpty();

    [Test]
    public async Task ITypeSystemRule_ClassifiesNothing() =>
        await Assert.That(Classifying(typeof(ITypeSystemRule))).IsEmpty();

    [Test]
    public async Task Rules_AreConstrainedToTheBase() {
        MethodInfo[] doors = [
            typeof(ITypeSystem).GetMethod(nameof(ITypeSystem.Rule))!,
            typeof(TypeSystemRules).GetMethod(nameof(TypeSystemRules.Add))!,
            typeof(TypeSystemRules).GetMethod(nameof(TypeSystemRules.Find))!,
        ];
        foreach (var door in doors)
            await Assert.That(door.GetGenericArguments().Single().GetGenericParameterConstraints()).Contains(typeof(ITypeSystemRule))
                .Because($"'{door.DeclaringType!.Name}.{door.Name}' accepts only a rule that implements the base");
    }

    [Test]
    public async Task Introspection_DeclaresNoRule() {
        var declared = typeof(ITypeSystemRule).Assembly.GetTypes()
            .Where(static type => type.Namespace is { } ns && (ns == "Poly.Introspection" || ns.StartsWith("Poly.Introspection.", StringComparison.Ordinal)))
            .Where(static type => type != typeof(ITypeSystemRule) && typeof(ITypeSystemRule).IsAssignableFrom(type))
            .Select(static type => type.FullName);

        await Assert.That(declared).IsEmpty().Because("a rule's owner declares it, outside Introspection");
    }

    // A member that answers what kind one definition is: a property of a classifying type, or a method taking exactly
    // one definition and returning one.
    private static List<string> Classifying(Type contract) {
        static bool Classifies(Type type) => type == typeof(bool) || type == typeof(int) || type.IsEnum
            || Nullable.GetUnderlyingType(type) is { } underlying && Classifies(underlying);
        return [
            .. contract.GetProperties(Declared).Where(static property => Classifies(property.PropertyType)).Select(static property => property.Name),
            .. contract.GetMethods(Declared)
                .Where(static method => !IsAccessor(method) && Classifies(method.ReturnType)
                    && method.GetParameters() is [{ ParameterType: var only }] && only == typeof(ITypeDefinition))
                .Select(static method => method.Name),
        ];
    }

    private static string Describe(ITypeConversion? conversion) => conversion is null
        ? "none"
        : $"{conversion.Kind} {conversion.SourceTypeDefinition.FullName}->{conversion.ResultTypeDefinition.FullName} {conversion.Method?.Name}";

    private static string Describe(ITypeOperator? op) => op is null
        ? "none"
        : $"{op.Kind} {op.DeclaringTypeDefinition.FullName}({string.Join(",", op.Parameters.Select(static p => p.ParameterTypeDefinition.FullName))})->{op.ResultTypeDefinition.FullName} {op.Method?.Name}";

    private interface IProbeRule : ITypeSystemRule;

    private sealed class ProbeRule : IProbeRule;

    private interface IOtherProbeRule : ITypeSystemRule;

    private sealed class BothRules : IProbeRule, IOtherProbeRule;
}