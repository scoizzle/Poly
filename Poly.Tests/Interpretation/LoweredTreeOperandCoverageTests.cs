using Poly.Interpretation;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Tests.DomainModeling.Lowering;

using static Poly.Tests.Interpretation.OperandExpect;

namespace Poly.Tests.Interpretation;

/// <summary>What a row expects analysis to do with mismatched operands.</summary>
public enum OperandExpect {
    /// <summary>Analysis records a replacement in the tree and the converted tree runs to the expected value.</summary>
    Converts,
    /// <summary>Analysis reports an Error inside the tree, and compile is refused with that diagnostic's message.</summary>
    Rejects,
    /// <summary>Analysis does not reject, and the tree compiles and runs to the expected value (an over-rejection guard).</summary>
    Accepts,
}

/// <summary>
/// One operand-agreement row: a lowered tree whose operands are typed <see cref="Parameter"/>s, what
/// analysis must do with it, and the result type and value a row that runs must produce.
/// <see cref="CommonType"/> is the literal the row states for its result type, with a one-line reason at the row;
/// <see cref="Owner"/> is a seed hint for the first red run only.
/// </summary>
public sealed record OperandCase(
    Type Kind, string Name, OperandExpect Expect, Func<Node> Build, object?[] Args,
    Type? CommonType = null, object? Value = null, string Owner = "NONE") {
    public override string ToString() => $"{Kind.Name}.{Name}";
}

/// <summary>
/// What one row did. <see cref="Analysis"/> is <c>unchanged</c> (no Error in the tree and no replacement on any node of
/// it), <c>converted</c> (a node replacement is recorded on a node of the tree) or <c>rejected</c> (an Error diagnostic
/// whose node is inside the tree; also an analysis that threw). <see cref="Compile"/> is <c>rejected-by-analysis</c>
/// (compile threw an <see cref="InvalidOperationException"/> carrying the first in-tree Error's message),
/// <c>backstop</c> (compile threw an <see cref="InvalidOperationException"/> without it), <c>compile-threw:Type</c> (any
/// other compile exception), <c>run-threw:Type</c> (it compiled and the run or the read threw), <c>wrong-value</c> (a
/// <c>Converts</c> or <c>Accepts</c> row that ran to a value other than the expected one), <c>ran</c> (a <c>Rejects</c>
/// row that compiled and ran) or <c>ok</c> (a <c>Converts</c> or <c>Accepts</c> row that ran, with the expected value
/// when the row states one). <see cref="Detail"/> is the message behind the outcome and is never part of the ledger.
/// </summary>
public sealed record OperandObservation(string Analysis, string Compile, string Detail) {
    public string Observed => $"analysis={Analysis};compile={Compile}";
}

public class LoweredTreeOperandCoverageTests {
    public sealed class Item(decimal price) {
        public decimal Price { get; set; } = price;
        public long Count { get; set; }
    }

    public static class Probe {
        public static decimal TakesDecimal(decimal value) => value;
        public static long TakesNumber(long value) => value;
    }

    static Parameter P<T>(string name) => new(name, TypeReference.To<T>());
    static Parameter Dec(string name = "d") => P<decimal>(name);
    static Parameter Num(string name = "n") => P<long>(name);
    static Parameter Txt(string name = "s") => P<string>(name);
    static Parameter Flt(string name = "f") => P<double>(name);
    static Parameter Flag(string name = "flag") => P<bool>(name);
    static Parameter OptNum(string name = "on") => P<long?>(name);
    static Parameter OptDec(string name = "od") => P<decimal?>(name);
    static Parameter Ent(string name = "e") => P<Item>(name);
    static Parameter NumArray(string name = "arr") => P<long[]>(name);
    static Node Wrap(params Node[] nodes) => new Block(nodes);
    static Node Pick(Node condition, Node ifTrue, Node ifFalse) => Wrap(new Conditional(condition, ifTrue, ifFalse));
    static Node Call(string method, Node argument) => Wrap(new Invoke(new Member(new ClrTypeReference(typeof(Probe)), method), argument));

    static Node Assign2(Node first, Node second) {
        var x = new Variable("x");
        return new Block([new Assignment(x, first), new Assignment(x, second), x], [x]);
    }

    static Node Switched(Node key, Node pattern, Node caseBody, Node defaultBody) =>
        Wrap(new SwitchStatement(key, [new SwitchCase(pattern, caseBody)], defaultBody));

    static OperandCase Row(Type kind, string name, OperandExpect expect, Func<Node> build, object?[] args,
        Type? type = null, object? value = null, string owner = "NONE") =>
        new(kind, name, expect, build, args, type, value, owner);

    static readonly OperandCase[] Rows = [
        // Add: common type is the wider arithmetic type, or string when one side is text.
        Row(typeof(Add), "DecimalNumber", Converts, () => Wrap(new Add(Dec(), Num())), [1.5m, 2L], typeof(decimal), 3.5m), // common type: long widens to decimal
        Row(typeof(Add), "NumberDecimal", Converts, () => Wrap(new Add(Num(), Dec())), [2L, 1.5m], typeof(decimal), 3.5m), // common type: long widens to decimal
        Row(typeof(Add), "TextNumber", Converts, () => Wrap(new Add(Txt(), Num())), ["a", 2L], typeof(string), "a2"), // common type: string concatenation
        Row(typeof(Add), "NumberText", Converts, () => Wrap(new Add(Num(), Txt())), [2L, "a"], typeof(string), "2a"), // common type: string concatenation
        Row(typeof(Add), "NumberNumber", Accepts, () => Wrap(new Add(Num(), Num("m"))), [2L, 3L], typeof(long), 5L), // common type: identical operand types
        Row(typeof(Add), "OptionalNumber", Accepts, () => Wrap(new Add(OptNum(), Num())), [3L, 2L], typeof(long?), 5L), // common type: nullable lift keeps long?
        Row(typeof(Add), "EntityNumber", Rejects, () => Wrap(new Add(Ent(), Num())), [new Item(1m), 2L]), // common type: none, an entity has no + with a number
        Row(typeof(Add), "Float64Decimal", Rejects, () => Wrap(new Add(Flt(), Dec())), [1.5, 1.5m]), // common type: none, C# defines no double + decimal
        Row(typeof(Add), "ConstantPairFolding", Converts, () => Wrap(new Add(new Constant(1.5m), new Constant(2L))), [], typeof(decimal), 3.5m), // common type: long widens to decimal; T-20 folding
        Row(typeof(Subtract), "DecimalNumber", Converts, () => Wrap(new Subtract(Dec(), Num())), [1.5m, 2L], typeof(decimal), -0.5m), // common type: long widens to decimal
        Row(typeof(Subtract), "TextNumber", Rejects, () => Wrap(new Subtract(Txt(), Num())), ["a", 2L]), // common type: none, text has no -
        Row(typeof(Multiply), "NumberDecimal", Converts, () => Wrap(new Multiply(Num(), Dec())), [2L, 1.5m], typeof(decimal), 3.0m), // common type: long widens to decimal
        Row(typeof(Multiply), "TextNumber", Rejects, () => Wrap(new Multiply(Txt(), Num())), ["a", 2L]), // common type: none, text has no *
        Row(typeof(Multiply), "Float64Decimal", Rejects, () => Wrap(new Multiply(Flt(), Dec())), [1.5, 1.5m]), // common type: none, C# defines no double * decimal
        Row(typeof(Divide), "DecimalNumber", Converts, () => Wrap(new Divide(Dec(), Num())), [1.5m, 2L], typeof(decimal), 0.75m), // common type: long widens to decimal
        Row(typeof(Divide), "TextNumber", Rejects, () => Wrap(new Divide(Txt(), Num())), ["a", 2L]), // common type: none, text has no /
        Row(typeof(Modulo), "DecimalNumber", Converts, () => Wrap(new Modulo(Dec(), Num())), [1.5m, 2L], typeof(decimal), 1.5m), // common type: long widens to decimal
        Row(typeof(Modulo), "TextNumber", Rejects, () => Wrap(new Modulo(Txt(), Num())), ["a", 2L]), // common type: none, text has no %

        // Comparison: operands must share a comparable type; the result is Boolean.
        Row(typeof(Equal), "DecimalNumber", Accepts, () => Wrap(new Equal(Dec(), Num())), [1.5m, 2L], typeof(bool), false), // common type: Boolean result, long widens to decimal
        Row(typeof(Equal), "TextNumber", Rejects, () => Wrap(new Equal(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not comparable
        Row(typeof(Equal), "OptionalNumber", Accepts, () => Wrap(new Equal(OptNum(), Num())), [3L, 2L], typeof(bool), false), // common type: Boolean result, lifted comparison
        Row(typeof(Equal), "EntityNumber", Rejects, () => Wrap(new Equal(Ent(), Num())), [new Item(1m), 2L]), // common type: none, an entity is not a number
        Row(typeof(Equal), "Float64Decimal", Rejects, () => Wrap(new Equal(Flt(), Dec())), [1.5, 1.5m]), // common type: none, C# defines no double == decimal
        Row(typeof(NotEqual), "TextNumber", Rejects, () => Wrap(new NotEqual(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not comparable
        Row(typeof(NotEqual), "DecimalNumber", Accepts, () => Wrap(new NotEqual(Dec(), Num())), [1.5m, 2L], typeof(bool), true), // common type: Boolean result, long widens to decimal
        Row(typeof(LessThan), "DecimalNumber", Accepts, () => Wrap(new LessThan(Dec(), Num())), [1.5m, 2L], typeof(bool), true), // common type: Boolean result, long widens to decimal
        Row(typeof(LessThan), "TextNumber", Rejects, () => Wrap(new LessThan(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not ordered
        Row(typeof(LessThan), "Float64Decimal", Rejects, () => Wrap(new LessThan(Flt(), Dec())), [1.5, 1.5m]), // common type: none, C# defines no double < decimal
        Row(typeof(LessThanOrEqual), "NumberDecimal", Accepts, () => Wrap(new LessThanOrEqual(Num(), Dec())), [2L, 1.5m], typeof(bool), false), // common type: Boolean result, long widens to decimal
        Row(typeof(LessThanOrEqual), "TextNumber", Rejects, () => Wrap(new LessThanOrEqual(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not ordered
        Row(typeof(GreaterThan), "NumberDecimal", Accepts, () => Wrap(new GreaterThan(Num(), Dec())), [2L, 1.5m], typeof(bool), true), // common type: Boolean result, long widens to decimal
        Row(typeof(GreaterThan), "TextNumber", Rejects, () => Wrap(new GreaterThan(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not ordered
        Row(typeof(GreaterThanOrEqual), "DecimalNumber", Accepts, () => Wrap(new GreaterThanOrEqual(Dec(), Num())), [1.5m, 2L], typeof(bool), false), // common type: Boolean result, long widens to decimal
        Row(typeof(GreaterThanOrEqual), "TextNumber", Rejects, () => Wrap(new GreaterThanOrEqual(Txt(), Num())), ["a", 2L]), // common type: none, text and number are not ordered

        // Logic and bitwise: Boolean needs Boolean; bit operators need integral operands.
        Row(typeof(And), "BooleanNumber", Rejects, () => Wrap(new And(Flag(), Num())), [true, 2L]), // common type: none, a number is not a Boolean
        Row(typeof(And), "BooleanBoolean", Accepts, () => Wrap(new And(Flag(), Flag("flag2"))), [true, false], typeof(bool), false), // common type: Boolean
        Row(typeof(Or), "BooleanNumber", Rejects, () => Wrap(new Or(Flag(), Num())), [true, 2L]), // common type: none, a number is not a Boolean
        Row(typeof(Or), "NumberBoolean", Rejects, () => Wrap(new Or(Num(), Flag())), [2L, true]), // common type: none, a number is not a Boolean
        Row(typeof(Not), "Number", Rejects, () => Wrap(new Not(Num())), [2L]), // common type: none, a number is not a Boolean
        Row(typeof(Not), "Boolean", Accepts, () => Wrap(new Not(Flag())), [true], typeof(bool), false), // common type: Boolean
        Row(typeof(BitwiseAnd), "NumberNumber", Accepts, () => Wrap(new BitwiseAnd(Num(), Num("m"))), [2L, 3L], typeof(long), 2L), // common type: identical integral operands
        Row(typeof(BitwiseAnd), "NumberText", Rejects, () => Wrap(new BitwiseAnd(Num(), Txt())), [2L, "a"]), // common type: none, text has no &
        Row(typeof(BitwiseAnd), "NumberDecimal", Rejects, () => Wrap(new BitwiseAnd(Num(), Dec())), [2L, 1.5m]), // common type: none, decimal has no &
        Row(typeof(BitwiseOr), "NumberDecimal", Rejects, () => Wrap(new BitwiseOr(Num(), Dec())), [2L, 1.5m]), // common type: none, decimal has no |
        Row(typeof(BitwiseXor), "NumberText", Rejects, () => Wrap(new BitwiseXor(Num(), Txt())), [2L, "a"]), // common type: none, text has no ^
        Row(typeof(ShiftLeft), "NumberText", Rejects, () => Wrap(new ShiftLeft(Num(), Txt())), [2L, "a"]), // common type: none, a shift count is integral
        Row(typeof(ShiftLeft), "DecimalNumber", Rejects, () => Wrap(new ShiftLeft(Dec(), Num())), [1.5m, 2L]), // common type: none, decimal has no <<
        Row(typeof(ShiftRight), "TextNumber", Rejects, () => Wrap(new ShiftRight(Txt(), Num())), ["a", 2L]), // common type: none, text has no >>

        // Assignment: the value must convert implicitly to the destination's type.
        Row(typeof(Assignment), "VariableTextIntoNumber", Rejects, () => Assign2(Num(), Txt()), [2L, "a"]), // common type: none, text does not convert to a number
        Row(typeof(Assignment), "VariableNumberIntoDecimal", Converts, () => Assign2(Dec(), Num()), [1.5m, 2L], typeof(decimal), 2m), // common type: long widens to decimal
        Row(typeof(Assignment), "VariableDecimalIntoNumber", Rejects, () => Assign2(Num(), Dec()), [2L, 1.5m]), // common type: none, decimal narrows to long only by cast
        Row(typeof(Assignment), "VariableOptionalIntoRequired", Rejects, () => Assign2(Num(), OptNum()), [2L, 3L]), // common type: none, long? does not convert to long
        Row(typeof(Assignment), "VariableRequiredIntoOptional", Accepts, () => Assign2(OptNum(), Num()), [3L, 2L], typeof(long?), 2L), // common type: long converts to long?
        Row(typeof(Assignment), "VariableEntityIntoScalar", Rejects, () => Assign2(Num(), Ent()), [2L, new Item(1m)]), // common type: none, an entity is not a number
        Row(typeof(Assignment), "MemberTextIntoNumber", Rejects, () => Wrap(new Assignment(new Member(Ent(), "Count"), Txt())), [new Item(1m), "a"]), // common type: none, text does not convert to long
        Row(typeof(Assignment), "MemberNumberIntoDecimal", Converts, () => { var e = Ent(); return Wrap(new Assignment(new Member(e, "Price"), Num()), new Member(e, "Price")); }, [new Item(1m), 2L], typeof(decimal), 2m), // common type: long widens to decimal
        Row(typeof(Assignment), "IndexTextIntoNumberArray", Rejects, () => Wrap(new Assignment(new IndexAccess(NumArray(), Num("i")), Txt())), [new long[] { 1 }, 0L, "a"]), // common type: none, text does not convert to long
        Row(typeof(Assignment), "IndexNumberIntoNumberArray", Accepts, () => { var a = NumArray(); var i = Num("i"); return Wrap(new Assignment(new IndexAccess(a, i), Num("v")), new IndexAccess(a, i)); }, [new long[] { 1 }, 0L, 7L], typeof(long), 7L), // common type: identical element and value types

        // Conditional: both arms must reach one common type (or null for a reference arm).
        Row(typeof(Conditional), "DecimalNumber", Converts, () => Pick(Flag(), Dec(), Num()), [false, 1.5m, 2L], typeof(decimal), 2m, "#605 T-09"), // common type: the integral arm widens to decimal
        Row(typeof(Conditional), "NumberDecimal", Converts, () => Pick(Flag(), Num(), Dec()), [true, 2L, 1.5m], typeof(decimal), 2m, "#605 T-09"), // common type: the integral arm widens to decimal
        Row(typeof(Conditional), "TextNumber", Rejects, () => Pick(Flag(), Txt(), Num()), [false, "a", 2L]), // common type: none, text and number share no type
        Row(typeof(Conditional), "DecimalText", Rejects, () => Pick(Flag(), Dec(), Txt()), [false, 1.5m, "a"]), // common type: none, decimal and text share no type
        Row(typeof(Conditional), "EntityDecimal", Rejects, () => Pick(Flag(), Ent(), Dec()), [false, new Item(1m), 1.5m]), // common type: none, an entity and a decimal share no type
        Row(typeof(Conditional), "NumberNumber", Accepts, () => Pick(Flag(), Num(), Num("m")), [false, 2L, 3L], typeof(long), 3L), // common type: identical arm types
        Row(typeof(Conditional), "NullableThenNumberNullTaken", Accepts, () => Pick(Flag(), OptNum(), Num()), [true, null, 5L], typeof(long?), null, "#803 T-06"), // common type: long? (the long arm converts to long?)
        Row(typeof(Conditional), "NumberThenNullableNullTaken", Accepts, () => Pick(Flag(), Num(), OptNum()), [false, 5L, null], typeof(long?), null, "#803 T-06"), // common type: long? (the long arm converts to long?)
        Row(typeof(Conditional), "ThreeArmsDecimalNumberDecimal", Converts, () => Pick(Flag("f1"), Dec(), Pick(Flag("f2"), Num(), Dec("d2"))), [false, 1.5m, true, 2L, 3.5m], typeof(decimal), 2m, "#605 T-09"), // common type: the nested integral arm widens to decimal
        Row(typeof(Conditional), "ThreeArmsTextInside", Rejects, () => Pick(Flag("f1"), Dec(), Pick(Flag("f2"), Num(), Txt())), [false, 1.5m, true, 2L, "a"]), // common type: none, the nested arms share no type
        Row(typeof(Conditional), "NumberCondition", Rejects, () => Pick(Num("c"), Num(), Num("m")), [2L, 2L, 3L]), // common type: none, the condition is not Boolean

        // Coalesce: the right arm must convert to the left arm's underlying type.
        Row(typeof(Coalesce), "OptionalDecimalNumber", Converts, () => Wrap(new Coalesce(OptDec(), Num())), [null, 2L], typeof(decimal), 2m, "#605 T-09"), // common type: decimal (the long arm widens)
        Row(typeof(Coalesce), "OptionalNumberDecimal", Converts, () => Wrap(new Coalesce(OptNum(), Dec())), [null, 1.5m], typeof(decimal), 1.5m, "#605 T-09"), // common type: decimal (long? coalesced with decimal widens)
        Row(typeof(Coalesce), "TextNumber", Rejects, () => Wrap(new Coalesce(Txt(), Num())), ["a", 2L]), // common type: none, text and number share no type
        Row(typeof(Coalesce), "OptionalNumberNumber", Accepts, () => Wrap(new Coalesce(OptNum(), Num())), [null, 2L], typeof(long), 2L), // common type: long
        Row(typeof(Coalesce), "EntityDecimal", Rejects, () => Wrap(new Coalesce(Ent(), Dec())), [null, 1.5m]), // common type: none, an entity and a decimal share no type

        // SwitchStatement in value position: case and default bodies merge into one value. As a statement nothing merges.
        // Arguments bind in emission order: key, default body, then the case bodies.
        Row(typeof(SwitchStatement), "DecimalCaseNumberDefault", Converts, () => Switched(Num("key"), new Constant(1L), Dec(), Num()), [0L, 2L, 1.5m], typeof(decimal), 2m, "#805 T-21"), // common type: the integral arm widens to decimal
        Row(typeof(SwitchStatement), "NumberCaseDecimalDefault", Converts, () => Switched(Num("key"), new Constant(1L), Num(), Dec()), [1L, 1.5m, 2L], typeof(decimal), 2m, "#805 T-21"), // common type: the integral arm widens to decimal
        Row(typeof(SwitchStatement), "TextCaseNumberDefault", Rejects, () => Switched(Num("key"), new Constant(1L), Txt(), Num()), [0L, 2L, "a"]), // common type: none, the bodies share no type
        Row(typeof(SwitchStatement), "NumberCaseNumberDefault", Accepts, () => Switched(Num("key"), new Constant(1L), Num(), Num("m")), [0L, 3L, 2L], typeof(long), 3L), // common type: identical body types
        Row(typeof(SwitchStatement), "PatternTextAgainstNumberSubject", Rejects, () => Switched(Num("key"), new Constant("a"), Num(), Num("m")), [1L, 3L, 2L]), // common type: none, a text pattern never matches a number
        Row(typeof(SwitchStatement), "StatementMixedAssignments", Accepts, () => {
            var vd = new Variable("vd");
            var vn = new Variable("vn");
            return new Block([
                new Assignment(vd, Dec("dz")), new Assignment(vn, Num("nz")),
                new SwitchStatement(Num("key"), [new SwitchCase(new Constant(1L), new Assignment(vd, Dec()))], new Assignment(vn, Num())),
                vd], [vd, vn]);
        }, [0m, 0L, 1L, 2L, 1.5m], typeof(decimal), 1.5m), // common type: none merged, the bodies are statements; the variable reads back as decimal

        // Return, If, While, For, DoWhile, ForEach: a returned value agrees with the others, a condition is Boolean.
        Row(typeof(Return), "DecimalThenText", Rejects, () => Wrap(new IfStatement(Flag(), new Return(Dec())), new Return(Txt())), [true, 1.5m, "a"]), // common type: none, the returns share no type
        Row(typeof(Return), "DecimalThenNumber", Converts, () => Wrap(new IfStatement(Flag(), new Return(Dec())), new Return(Num())), [false, 1.5m, 2L], typeof(decimal), 2m), // common type: the integral return widens to decimal
        Row(typeof(Return), "NumberThenNumber", Accepts, () => Wrap(new IfStatement(Flag(), new Return(Num())), new Return(Num("m"))), [false, 2L, 3L], typeof(long), 3L), // common type: identical return types
        Row(typeof(IfStatement), "NumberCondition", Rejects, () => Wrap(new IfStatement(Num("c"), new Return(Num())), new Return(Num("m"))), [2L, 3L, 4L]), // common type: none, the condition is not Boolean
        Row(typeof(IfStatement), "BooleanCondition", Accepts, () => Wrap(new IfStatement(Flag(), new Return(Num())), new Return(Num("m"))), [false, 2L, 3L], typeof(long), 3L), // common type: long
        Row(typeof(WhileLoop), "NumberCondition", Rejects, () => Wrap(new WhileLoop(Num("c"), new Return(Num())), new Return(Num("m"))), [2L, 3L, 4L]), // common type: none, the condition is not Boolean
        Row(typeof(ForLoop), "NumberCondition", Rejects, () => Wrap(new ForLoop(null, Num("c"), null, new Return(Num())), new Return(Num("m"))), [2L, 3L, 4L]), // common type: none, the condition is not Boolean
        Row(typeof(DoWhileLoop), "NumberCondition", Rejects, () => Wrap(new DoWhileLoop(new Return(Num()), Num("c")), new Return(Num("m"))), [2L, 3L, 4L]), // common type: none, the condition is not Boolean
        Row(typeof(ForEachLoop), "DeclaredTextElementNumber", Rejects, () => {
            var item = new Variable("item");
            return new Block([new ForEachLoop(item, NumArray(), item, DeclaredType: TypeReference.To<string>()), Num()], [item]);
        }, [new long[] { 1 }, 2L]), // common type: none, a long element does not convert to text
        Row(typeof(ForEachLoop), "DeclaredNumberElementDecimal", Rejects, () => {
            var item = new Variable("item");
            return new Block([new ForEachLoop(item, P<decimal[]>("arr"), item, DeclaredType: TypeReference.To<long>()), Num()], [item]);
        }, [new[] { 1.5m }, 2L]), // common type: none, a decimal element narrows to long only by cast
        Row(typeof(ForEachLoop), "DeclaredDecimalElementNumber", Accepts, () => {
            var acc = new Variable("acc");
            var item = new Variable("item");
            return new Block([new Assignment(acc, Dec("dz")), new ForEachLoop(item, NumArray(), new Assignment(acc, item), DeclaredType: TypeReference.To<decimal>()), acc], [acc, item]);
        }, [0m, new long[] { 7 }], typeof(decimal), 7m), // common type: a long element widens to the declared decimal

        // Casts and constructors: legality of the conversion, and each argument against its parameter.
        Row(typeof(TypeCast), "TextToNumber", Rejects, () => Wrap(new TypeCast(Txt(), TypeReference.To<long>())), ["a"]), // common type: none, text has no explicit conversion to long
        Row(typeof(TypeCast), "DecimalToNumber", Accepts, () => Wrap(new TypeCast(Dec(), TypeReference.To<long>())), [1.5m], typeof(long), 1L), // common type: long (explicit narrowing)
        Row(typeof(TypeCast), "NumberToDecimal", Accepts, () => Wrap(new TypeCast(Num(), TypeReference.To<decimal>())), [2L], typeof(decimal), 2m), // common type: decimal (explicit widening)
        Row(typeof(TypeCast), "EntityToNumber", Rejects, () => Wrap(new TypeCast(Ent(), TypeReference.To<long>())), [new Item(1m)]), // common type: none, an entity has no conversion to long
        Row(typeof(TypeAs), "NumberToText", Rejects, () => Wrap(new TypeAs(Num(), TypeReference.To<string>())), [2L]), // common type: none, a long is never a string
        Row(typeof(TypeAs), "TextToText", Accepts, () => Wrap(new TypeAs(Txt(), TypeReference.To<string>())), ["a"], typeof(string), "a"), // common type: string
        Row(typeof(New), "ArgumentTextForDecimalParameter", Rejects, () => Wrap(new New(TypeReference.To<Item>(), Txt())), ["a"]), // common type: none, text does not convert to decimal
        Row(typeof(New), "ArgumentNumberForDecimalParameter", Converts, () => Wrap(new New(TypeReference.To<Item>(), Num())), [2L]), // common type: the argument widens to decimal
        Row(typeof(New), "ArgumentDecimalForDecimalParameter", Accepts, () => Wrap(new New(TypeReference.To<Item>(), Dec())), [1.5m]), // common type: identical parameter type
        Row(typeof(New), "ArgumentEntityForDecimalParameter", Rejects, () => Wrap(new New(TypeReference.To<Item>(), Ent())), [new Item(1m)]), // common type: none, an entity does not convert to decimal
        Row(typeof(NewArray), "LengthText", Rejects, () => Wrap(new NewArray(TypeReference.To<long>(), Txt())), ["a"]), // common type: none, a length is integral
        Row(typeof(NewArray), "LengthDecimal", Rejects, () => Wrap(new NewArray(TypeReference.To<long>(), Dec())), [1.5m]), // common type: none, a decimal length narrows only by cast
        Row(typeof(NewArray), "LengthNumber", Accepts, () => Wrap(new NewArray(TypeReference.To<long>(), Num())), [2L]), // common type: long[] (no value checked)
        Row(typeof(IndexAccess), "IndexText", Rejects, () => Wrap(new IndexAccess(NumArray(), Txt())), [new long[] { 5, 6 }, "a"]), // common type: none, an array index is integral
        Row(typeof(IndexAccess), "IndexDecimal", Rejects, () => Wrap(new IndexAccess(NumArray(), Dec())), [new long[] { 5, 6 }, 1.5m]), // common type: none, a decimal index narrows only by cast
        Row(typeof(IndexAccess), "IndexOptional", Rejects, () => Wrap(new IndexAccess(NumArray(), OptNum())), [new long[] { 5, 6 }, 1L]), // common type: none, long? is not an index
        Row(typeof(IndexAccess), "IndexNumber", Accepts, () => Wrap(new IndexAccess(NumArray(), Num())), [new long[] { 5, 6 }, 1L], typeof(long), 6L), // common type: long
        Row(typeof(InterpolatedString), "MixedParts", Accepts, () => Wrap(new InterpolatedString(["", "-", ""], [Num(), Txt()])), [2L, "a"], typeof(string), "2-a"), // common type: string (every hole stringifies)

        // Invoke: each argument against its parameter, and a lambda's body and arguments.
        Row(typeof(Invoke), "ArgumentNumberForDecimalParameter", Converts, () => Call("TakesDecimal", Num()), [2L], typeof(decimal), 2m), // common type: the argument widens to decimal
        Row(typeof(Invoke), "ArgumentDecimalForNumberParameter", Rejects, () => Call("TakesNumber", Dec()), [1.5m]), // common type: none, decimal narrows to long only by cast
        Row(typeof(Invoke), "ArgumentTextForNumberParameter", Rejects, () => Call("TakesNumber", Txt()), ["a"]), // common type: none, text does not convert to long
        Row(typeof(Invoke), "ArgumentEntityForNumberParameter", Rejects, () => Call("TakesNumber", Ent()), [new Item(1m)]), // common type: none, an entity is not a number
        Row(typeof(Invoke), "ArgumentOptionalForNumberParameter", Rejects, () => Call("TakesNumber", OptNum()), [3L]), // common type: none, long? does not convert to long
        Row(typeof(Invoke), "ArgumentNumberForNumberParameter", Accepts, () => Call("TakesNumber", Num()), [2L], typeof(long), 2L), // common type: long
        Row(typeof(Lambda), "BodyAddsDecimalAndNumber", Converts, () => Wrap(new Invoke(new Lambda([Dec("a"), Num("b")], new Add(Dec("a"), Num("b"))), Dec(), Num())), [1.5m, 2L], typeof(decimal), 3.5m), // common type: long widens to decimal in the body
        Row(typeof(Lambda), "ArgumentNumberForDecimalParameter", Converts, () => Wrap(new Invoke(new Lambda([Dec("a")], Dec("a")), Num())), [2L], typeof(decimal), 2m), // common type: the argument widens to decimal
        Row(typeof(Lambda), "ArgumentTextForNumberParameter", Rejects, () => Wrap(new Invoke(new Lambda([Num("a")], Num("a")), Txt())), ["a"]), // common type: none, text does not convert to long
    ];

    const string Declaration = "declaration: carries a name, a type and a body, not an operand pair";
    const string TypeReferenceReason = "type reference: names a type, carries no value";
    const string Jump = "jump: moves control, carries no value";

    static readonly Dictionary<Type, string> NotCombining = new() {
        [typeof(Comment)] = "text only: no operand",
        [typeof(Await)] = "one operand: awaits it",
        [typeof(BitwiseNot)] = "one operand: complements it",
        [typeof(Block)] = "sequence: its value is its last node, no operand pair",
        [typeof(Constant)] = "leaf: a literal value",
        [typeof(Default)] = "default value of one type: no operand",
        [typeof(Member)] = "one receiver and a name: no operand pair",
        [typeof(NullForgiving)] = "one operand: asserts it non-null",
        [typeof(Parameter)] = "leaf: a declared binding",
        [typeof(ParameterReference)] = "leaf: a reference to a declared parameter",
        [typeof(PopCount)] = "one operand: counts its set bits",
        [typeof(StridedSetBits)] = "writes an array by a stride: its operands are integral by construction",
        [typeof(SuspendNode)] = "one operand: suspends with it",
        [typeof(ThisReference)] = "leaf: the current instance",
        [typeof(ThrowExpression)] = "one operand: throws it, as an expression",
        [typeof(ThrowStatement)] = "one operand: throws it, as a statement",
        [typeof(TryCatchFinally)] = "blocks only: no operand pair",
        [typeof(TypeIs)] = "one operand tested against a type: yields Boolean, nothing to merge",
        [typeof(TypeOf)] = "the Type object of one type: no operand",
        [typeof(UnaryMinus)] = "one operand: negates it",
        [typeof(UsingStatement)] = "one resource and a body: no operand pair",
        [typeof(Variable)] = "leaf: a named binding",
        [typeof(YieldStatement)] = "one operand: yields it",
        [typeof(AttributeNode)] = Declaration,
        [typeof(AttributedNode)] = Declaration,
        [typeof(BaseConstructorInvocationNode)] = Declaration,
        [typeof(CompilationUnitNode)] = Declaration,
        [typeof(ConstructorDefinitionNode)] = Declaration,
        [typeof(FieldDefinitionNode)] = Declaration,
        [typeof(LabelDeclaration)] = Declaration,
        [typeof(MethodDefinitionNode)] = Declaration,
        [typeof(PropertyDefinitionNode)] = Declaration,
        [typeof(PropertyGetterDefinitionNode)] = Declaration,
        [typeof(PropertyInitializerDefinitionNode)] = Declaration,
        [typeof(PropertySetterDefinitionNode)] = Declaration,
        [typeof(TypeDefinitionNode)] = Declaration,
        [typeof(ClrTypeReference)] = TypeReferenceReason,
        [typeof(CollectionTypeReference)] = TypeReferenceReason,
        [typeof(MapTypeReference)] = TypeReferenceReason,
        [typeof(NamedTypeReference)] = TypeReferenceReason,
        [typeof(OptionalTypeReference)] = TypeReferenceReason,
        [typeof(PrimitiveTypeReference)] = TypeReferenceReason,
        [typeof(ResolvedTypeReference)] = TypeReferenceReason,
        [typeof(TypeDefinitionReference)] = TypeReferenceReason,
        [typeof(TypeReference)] = TypeReferenceReason,
        [typeof(UnionTypeReference)] = TypeReferenceReason,
        [typeof(BreakStatement)] = Jump,
        [typeof(ContinueStatement)] = Jump,
        [typeof(GotoStatement)] = Jump,
    };

    /// <summary>The kinds <c>SyntaxTypeCompatibilityAnalyzer.Analyze</c> has a case for: each must have rows.</summary>
    static readonly Type[] CompatibilityKinds = [
        typeof(Equal), typeof(NotEqual), typeof(LessThan), typeof(LessThanOrEqual), typeof(GreaterThan), typeof(GreaterThanOrEqual),
        typeof(Add), typeof(Subtract), typeof(Multiply), typeof(Divide), typeof(Modulo), typeof(TypeAs), typeof(New),
        typeof(Not), typeof(And), typeof(Or), typeof(Assignment), typeof(Invoke), typeof(TypeCast), typeof(ForEachLoop),
    ];

    static readonly Lazy<IReadOnlyDictionary<string, OperandObservation>> Observations = new(
        () => Rows.ToDictionary(row => Key(row), row => Observe(row), StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    static string Key(OperandCase row) => row.ToString();

    public static IEnumerable<Func<OperandCase>> RowData() => Rows.Select(row => (Func<OperandCase>)(() => row));

    static IEnumerable<Node> Walk(Node node) {
        yield return node;
        foreach (var child in node.Children)
            if (child is not null)
                foreach (var inner in Walk(child))
                    yield return inner;
    }

    static OperandObservation Observe(OperandCase row) {
        Node root;
        AnalysisResult analysis;
        try {
            root = row.Build();
            analysis = Interpreter.Analyze(root);
        }
        catch (Exception ex) {
            return new("rejected", $"compile-threw:{ex.GetType().Name}", ex.Message);
        }

        var tree = new HashSet<Node>(Walk(root), System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var firstError = analysis.Diagnostics.FirstOrDefault(
            d => d.Severity == DiagnosticSeverity.Error && d.Node is { } node && tree.Contains(node));
        var verdict = firstError is not null ? "rejected"
            : tree.Any(node => analysis.GetNodeReplacement(node) is not null) ? "converted"
            : "unchanged";

        VmProgram program;
        try {
            program = Interpreter.Compile(root, analysis);
        }
        catch (InvalidOperationException ex) {
            var byAnalysis = firstError is not null && ex.Message.Contains(firstError.Message, StringComparison.Ordinal);
            return new(verdict, byAnalysis ? "rejected-by-analysis" : "backstop", ex.Message);
        }
        catch (Exception ex) {
            return new(verdict, $"compile-threw:{ex.GetType().Name}", ex.Message);
        }

        try {
            using var exec = Interpreter.Execute(program, state => state.SetArgs(row.Args));
            if (row.Expect == Rejects)
                return new(verdict, "ran", "");
            if (row.CommonType is null)
                return new(verdict, "ok", "");
            var read = typeof(ExecutionResult).GetMethod(nameof(ExecutionResult.GetValue))!
                .MakeGenericMethod(row.CommonType).Invoke(exec, null);
            return Equals(read, row.Value)
                ? new(verdict, "ok", "")
                : new(verdict, "wrong-value", $"read '{read ?? "null"}', expected '{row.Value ?? "null"}'");
        }
        catch (Exception ex) {
            var cause = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return new(verdict, $"run-threw:{cause.GetType().Name}", cause.Message);
        }
    }

    static bool Met(OperandCase row, OperandObservation seen) => row.Expect switch {
        Converts => seen is { Analysis: "converted", Compile: "ok" },
        Accepts => seen is { Analysis: not "rejected", Compile: "ok" },
        _ => seen is { Analysis: "rejected", Compile: "rejected-by-analysis" },
    };

    static string Repro(OperandCase row) {
        var root = row.Build();
        var operands = Walk(root).OfType<Parameter>().Select(p => $"{p.Name}: {p.TypeReference}").Distinct();
        return $"{root} where {string.Join(", ", operands)}";
    }

    static string SeedLine(OperandCase row, OperandObservation seen) =>
        $"{row.Kind.Name} | {row.Name} | {row.Expect} | {seen.Observed} | {Repro(row)} | {row.Owner} | detail: {seen.Detail}";

    static IEnumerable<(OperandCase Row, OperandObservation Seen)> LiveGaps() =>
        Rows.Select(row => (Row: row, Seen: Observations.Value[Key(row)])).Where(pair => !Met(pair.Row, pair.Seen));

    [Test]
    public async Task EveryNodeFactsKind_IsClassified() {
        var combining = Rows.Select(row => row.Kind).ToHashSet();
        var problems = new List<string>();
        foreach (var kind in NodeFacts.All.Keys) {
            var combines = combining.Contains(kind);
            var notCombining = NotCombining.ContainsKey(kind);
            if (combines && notCombining)
                problems.Add($"{kind.Name}: both a combining kind and not combining");
            if (!combines && !notCombining)
                problems.Add($"{kind.Name}: unclassified");
        }
        foreach (var kind in combining.Concat(NotCombining.Keys).Where(kind => !NodeFacts.All.ContainsKey(kind)))
            problems.Add($"{kind.Name}: not in NodeFacts.All (stale)");
        foreach (var kind in CompatibilityKinds.Where(kind => NotCombining.ContainsKey(kind) || !combining.Contains(kind)))
            problems.Add($"{kind.Name}: the compatibility analyzer has a case for it, so it needs rows");
        foreach (var group in NotCombining.GroupBy(pair => pair.Value).Where(group => group.Count() > 1))
            if (group.Key is not (Declaration or TypeReferenceReason or Jump))
                problems.Add($"'{group.Key}' is shared by {group.Count()} kinds; only declaration, type-reference and jump kinds may group a reason");

        await Assert.That(problems).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RowData))]
    public async Task OperandRow_Analysis_ConvertsOrRejects_BeforeCompile(OperandCase row) {
        var seen = Observations.Value[Key(row)];
        if (Met(row, seen))
            return;

        var ledgered = KnownOperandGaps.All.FirstOrDefault(gap => gap.Kind == row.Kind.Name && gap.Case == row.Name);
        if (ledgered is null)
            Assert.Fail($"unledgered gap: {SeedLine(row, seen)}");
        await Assert.That(ledgered!.Observed).IsEqualTo(seen.Observed);
    }

    [Test]
    public async Task GapLedger_IsExactlyTheLiveGaps() {
        var live = LiveGaps().ToList();
        var drifts = OperandGapRatchet.Drift(KnownOperandGaps.All, live.Select(pair => (pair.Row.Kind.Name, pair.Row.Name, pair.Seen.Observed)));
        var problems = drifts.Select(drift => $"{drift.Kind}: {drift.Detail}").ToList();
        problems.AddRange(KnownOperandGaps.All
            .Where(gap => gap.Issue <= 0 || !DomainParityLedgerTests.IsSliceId(gap.Owner))
            .Select(gap => $"{gap.Kind}.{gap.Case}: needs an Issue above 0 and a slice-id Owner"));
        if (problems.Count > 0) {
            var seeds = live.Where(pair => KnownOperandGaps.All.All(gap => gap.Kind != pair.Row.Kind.Name || gap.Case != pair.Row.Name))
                .Select(pair => SeedLine(pair.Row, pair.Seen));
            var summary = string.Join(", ", drifts.GroupBy(drift => drift.Kind).Select(group => $"{group.Key}={group.Count()}"));
            Assert.Fail($"{live.Count} live gap(s) of {Rows.Length} rows; ledger drift: {summary}\n"
                + "Kind | Case | Expect | Observed | repro tree | existing issue+owner or NONE | detail\n"
                + string.Join("\n", seeds) + "\n"
                + string.Join("\n", problems.Where(problem => !problem.StartsWith("Widened", StringComparison.Ordinal))));
        }
        await Assert.That(problems).IsEmpty();
    }

    [Test]
    public async Task GapLedger_RowsAreSortedAndUnique() {
        var keys = KnownOperandGaps.All.Select(gap => (gap.Kind, gap.Case)).ToList();
        var sorted = keys.OrderBy(key => key.Kind, StringComparer.Ordinal).ThenBy(key => key.Case, StringComparer.Ordinal).ToList();
        await Assert.That(keys.SequenceEqual(sorted)).IsTrue();
        await Assert.That(keys.Distinct().Count()).IsEqualTo(keys.Count);
        await Assert.That(Rows.Select(Key).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(Rows.Length);
    }

    [Test]
    public async Task Ratchet_SyntheticRows_ReportWidenedStaleDeletedAndValueChanged() {
        const string a = "analysis=unchanged;compile=ok";
        const string b = "analysis=unchanged;compile=backstop";
        static KnownOperandGap Gap(string caseName, string observed) => new(1, "T-01", "Add", caseName, observed);

        var widened = OperandGapRatchet.Drift([], [("Add", "X", a)]);
        var stale = OperandGapRatchet.Drift([Gap("X", a), Gap("Y", a)], [("Add", "Y", a)]);
        var deleted = OperandGapRatchet.Drift([Gap("X", a)], []);
        var changed = OperandGapRatchet.Drift([Gap("X", a)], [("Add", "X", b)]);
        var exact = OperandGapRatchet.Drift([Gap("X", a)], [("Add", "X", a)]);

        await Assert.That(widened.Select(drift => drift.Kind)).IsEquivalentTo([ParityDriftKind.Widened]);
        await Assert.That(stale.Select(drift => drift.Kind)).IsEquivalentTo([ParityDriftKind.Stale]);
        await Assert.That(deleted.Select(drift => drift.Kind)).IsEquivalentTo([ParityDriftKind.Deleted]);
        await Assert.That(changed.Select(drift => drift.Kind)).IsEquivalentTo([ParityDriftKind.ValueChanged]);
        await Assert.That(exact).IsEmpty();
    }
}