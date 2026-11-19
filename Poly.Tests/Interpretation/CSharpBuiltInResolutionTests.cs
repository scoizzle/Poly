using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// C#'s built-in operators and conversions, resolved by C#'s type system (#998 T10). Pinned before the built-ins moved
/// out of the CLR host so the move changes no answer: "built-in" means the operator or conversion names no method (C#
/// applies it itself), "declared" names the type's own <c>op_*</c> member.
/// </summary>
public class CSharpBuiltInResolutionTests {
    private static readonly CSharpTypeSystem CSharp = CSharpTypeSystem.Shared;
    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    private static string Describe(ITypeOperator? op) => op is null
        ? "none"
        : $"{op.DeclaringTypeDefinition.Name}({string.Join(", ", op.Parameters.Select(static p => p.ParameterTypeDefinition.Name))}) -> {op.ResultTypeDefinition.Name} {(op.Method is null ? "built-in" : "declared")}";

    private static string Describe(ITypeConversion? conversion) => conversion is null
        ? "none"
        : $"{conversion.Kind} {(conversion.Method is null ? "built-in" : "declared")}";

    [Test]
    [Arguments(OperatorKind.LessThan, typeof(double), typeof(double), "Double(Double, Double) -> Boolean built-in")]
    [Arguments(OperatorKind.LessThan, typeof(float), typeof(float), "Single(Single, Single) -> Boolean built-in")]
    [Arguments(OperatorKind.Add, typeof(decimal), typeof(decimal), "Decimal(Decimal, Decimal) -> Decimal declared")]
    [Arguments(OperatorKind.Add, typeof(uint), typeof(int), "Int64(Int64, Int64) -> Int64 built-in")]
    [Arguments(OperatorKind.Add, typeof(int), typeof(long), "Int64(Int64, Int64) -> Int64 built-in")]
    [Arguments(OperatorKind.Add, typeof(nint), typeof(nint), "IntPtr(IntPtr, IntPtr) -> IntPtr built-in")]
    [Arguments(OperatorKind.Add, typeof(string), typeof(object), "String(String, Object) -> String built-in")]
    [Arguments(OperatorKind.Add, typeof(string), typeof(string), "String(String, String) -> String built-in")]
    [Arguments(OperatorKind.Equal, typeof(Uri), typeof(Uri), "Uri(Uri, Uri) -> Boolean declared")]
    [Arguments(OperatorKind.Equal, typeof(object), typeof(object), "Object(Object, Object) -> Boolean built-in")]
    [Arguments(OperatorKind.Equal, typeof(Action), typeof(Action), "Action(Action, Action) -> Boolean built-in")]
    [Arguments(OperatorKind.Add, typeof(TimeSpan), typeof(TimeSpan), "TimeSpan(TimeSpan, TimeSpan) -> TimeSpan declared")]
    [Arguments(OperatorKind.Add, typeof(int?), typeof(int), "none")]
    [Arguments(OperatorKind.BitwiseOr, typeof(DayOfWeek), typeof(DayOfWeek), "none")]
    [Arguments(OperatorKind.ShiftLeft, typeof(int), typeof(long), "Int32(Int32, Int64) -> Int32 built-in")]
    [Arguments(OperatorKind.And, typeof(bool), typeof(bool), "Boolean(Boolean, Boolean) -> Boolean built-in")]
    [Arguments(OperatorKind.Add, typeof(decimal), typeof(double), "none")]
    public async Task Operator_IsUnchanged(OperatorKind kind, Type left, Type right, string expected) =>
        await Assert.That(Describe(CSharp.Operator(kind, Clr(left), Clr(right)))).IsEqualTo(expected);

    [Test]
    [Arguments(OperatorKind.UnaryMinus, typeof(uint), "Int64(Int64) -> Int64 built-in")]
    [Arguments(OperatorKind.UnaryMinus, typeof(ulong), "none")]
    [Arguments(OperatorKind.Not, typeof(bool), "Boolean(Boolean) -> Boolean built-in")]
    [Arguments(OperatorKind.BitwiseNot, typeof(byte), "Int32(Int32) -> Int32 built-in")]
    public async Task UnaryOperator_IsUnchanged(OperatorKind kind, Type operand, string expected) =>
        await Assert.That(Describe(CSharp.Operator(kind, Clr(operand), null))).IsEqualTo(expected);

    [Test]
    [Arguments(typeof(int), typeof(long), "Implicit built-in")]
    [Arguments(typeof(long), typeof(int), "Explicit built-in")]
    [Arguments(typeof(int), typeof(decimal), "Implicit declared")]
    [Arguments(typeof(decimal), typeof(int), "Explicit declared")]
    [Arguments(typeof(nint), typeof(long), "Implicit built-in")]
    [Arguments(typeof(nint), typeof(int), "Explicit declared")]
    [Arguments(typeof(nint), typeof(decimal), "Implicit built-in")]
    [Arguments(typeof(int), typeof(object), "Implicit built-in")]
    [Arguments(typeof(int), typeof(long?), "Implicit built-in")]
    [Arguments(typeof(char), typeof(int), "Implicit built-in")]
    [Arguments(typeof(int), typeof(char), "Explicit built-in")]
    [Arguments(typeof(string), typeof(int), "none")]
    [Arguments(typeof(int), typeof(int), "none")]
    public async Task Conversion_IsUnchanged(Type from, Type to, string expected) =>
        await Assert.That(Describe(CSharp.Conversion(Clr(from), Clr(to)))).IsEqualTo(expected);

    [Test]
    [Arguments(typeof(int))]
    [Arguments(typeof(double))]
    [Arguments(typeof(string))]
    [Arguments(typeof(bool))]
    [Arguments(typeof(nint))]
    public async Task ClrDefinition_ListsOnlyWhatItDeclares(Type type) {
        // A CLR type's own lists are its declarations (owner ruling 1): C#'s built-ins are C#'s rules, offered by its
        // type system, not members the type states (#998 T10). int declares no conversion and no operator.
        var definition = Clr(type);

        await Assert.That(definition.Conversions.Where(static c => c.Method is null).Select(static c => c.ResultTypeDefinition.Name)).IsEmpty();
        await Assert.That(definition.Operators.Where(static o => o.Method is null).Select(static o => o.Kind.ToString())).IsEmpty();
    }
}