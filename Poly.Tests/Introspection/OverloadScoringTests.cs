using System.Reflection;
using System.Text;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-16: which overload wins and what a parameter accepts, as master binds them. Every expectation here
/// was captured from origin/master before the rank table was replaced; none is generated from the code
/// under test. Where C# 12.6.4 (better function member) would answer differently the test says so: the
/// scorer is a tier sum that keeps the first of equal candidates, and that stays until a better-conversion
/// row exists.
/// </summary>
public class OverloadScoringTests {
    public enum ColorEnum { Red }

    public enum ShapeEnum { Circle }

    public enum ByteEnum : byte { Low }

    public enum LongEnum : long { Big }

    public readonly struct Meters {
        public Meters(double value) => Value = value;
        public double Value { get; }
        public static implicit operator Meters(double value) => new(value);
        public static explicit operator double(Meters meters) => meters.Value;
    }

    /// <summary>One lone method per CLR primitive: a parameter that accepts or rejects an argument type.</summary>
    public static class Lone {
        public static string Boolean(bool x) => "Boolean";
        public static string Int8(sbyte x) => "Int8";
        public static string Int16(short x) => "Int16";
        public static string Int32(int x) => "Int32";
        public static string Int64(long x) => "Int64";
        public static string UInt8(byte x) => "UInt8";
        public static string UInt16(ushort x) => "UInt16";
        public static string UInt32(uint x) => "UInt32";
        public static string UInt64(ulong x) => "UInt64";
        public static string Float32(float x) => "Float32";
        public static string Float64(double x) => "Float64";
        public static string Decimal(decimal x) => "Decimal";
        public static string String(string x) => "String";
        public static string Char(char x) => "Char";
        public static string DateTime(DateTime x) => "DateTime";
        public static string DateOnly(DateOnly x) => "DateOnly";
        public static string TimeOnly(TimeOnly x) => "TimeOnly";
        public static string TimeSpan(TimeSpan x) => "TimeSpan";
        public static string Guid(Guid x) => "Guid";
        public static string ByteArray(byte[] x) => "ByteArray";
        public static string Structure(object x) => "Structure";
        public static string Color(ColorEnum x) => "Color";
        public static string Shape(ShapeEnum x) => "Shape";
        public static string Low(ByteEnum x) => "Low";
        public static string Big(LongEnum x) => "Big";
        public static string NullableInt32(int? x) => "NullableInt32";
        public static string NullableInt64(long? x) => "NullableInt64";
        public static string NullableDecimal(decimal? x) => "NullableDecimal";
        public static string Metres(Meters x) => "Metres";
        public static string NInt(nint x) => "NInt";
        public static string NUInt(nuint x) => "NUInt";
    }

    /// <summary>Overload sets whose winner depends on the tier sum.</summary>
    public static class Overloads {
        public static string ObjectVsLong(object x) => "object";
        public static string ObjectVsLong(long x) => "long";
        public static string LongVsDecimal(long x) => "long";
        public static string LongVsDecimal(decimal x) => "decimal";
        public static string LongVsDouble(long x) => "long";
        public static string LongVsDouble(double x) => "double";
        public static string MetersVsDecimal(Meters x) => "Meters";
        public static string MetersVsDecimal(decimal x) => "decimal";
    }

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    /// <summary>The 21 primitives the master matrices were captured over; T-18b's native integers are pinned apart (lock amendment 9).</summary>
    private static readonly Type[] Primitives = [.. Enum.GetValues<Prim>().Where(static p => p <= Prim.Structure).Select(static p => p.GetClrType()!)];

    private static readonly Type[] Natives = [typeof(nint), typeof(nuint)];

    private static readonly Type[] EnumsAndNumbers = [
        typeof(ColorEnum), typeof(ShapeEnum), typeof(ByteEnum), typeof(LongEnum),
        typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort), typeof(uint), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(char),
    ];

    private static readonly Type[] Nullables = [
        typeof(int), typeof(long), typeof(decimal), typeof(double), typeof(int?), typeof(long?), typeof(decimal?),
    ];

    private static string Accepts(string parameter, Type argument) =>
        Clr(typeof(Lone)).FindMatchingMethodOverloads(parameter, [Clr(argument)]).Any() ? "A" : ".";

    private static string Row(string parameter, IEnumerable<Type> arguments) =>
        string.Concat(arguments.Select(argument => Accepts(parameter, argument)));

    /// <summary>The names the primitive matrix and the enum and nullable matrices use for a type.</summary>
    private static string ParameterName(Type type) => type switch {
        _ when type == typeof(bool) => "Boolean",
        _ when type == typeof(sbyte) => "Int8",
        _ when type == typeof(short) => "Int16",
        _ when type == typeof(int) => "Int32",
        _ when type == typeof(long) => "Int64",
        _ when type == typeof(byte) => "UInt8",
        _ when type == typeof(ushort) => "UInt16",
        _ when type == typeof(uint) => "UInt32",
        _ when type == typeof(ulong) => "UInt64",
        _ when type == typeof(float) => "Float32",
        _ when type == typeof(double) => "Float64",
        _ when type == typeof(decimal) => "Decimal",
        _ when type == typeof(string) => "String",
        _ when type == typeof(char) => "Char",
        _ when type == typeof(DateTime) => "DateTime",
        _ when type == typeof(DateOnly) => "DateOnly",
        _ when type == typeof(TimeOnly) => "TimeOnly",
        _ when type == typeof(TimeSpan) => "TimeSpan",
        _ when type == typeof(Guid) => "Guid",
        _ when type == typeof(byte[]) => "ByteArray",
        _ when type == typeof(object) => "Structure",
        _ when type == typeof(ColorEnum) => "Color",
        _ when type == typeof(ShapeEnum) => "Shape",
        _ when type == typeof(ByteEnum) => "Low",
        _ when type == typeof(LongEnum) => "Big",
        _ when type == typeof(int?) => "NullableInt32",
        _ when type == typeof(long?) => "NullableInt64",
        _ when type == typeof(decimal?) => "NullableDecimal",
        _ when type == typeof(Meters) => "Metres",
        _ when type == typeof(nint) => "NInt",
        _ when type == typeof(nuint) => "NUInt",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    internal static string PrimitiveMatrix() => Matrix(Primitives, Primitives);

    /// <summary>T-18b: the native-integer rows over every primitive and native argument, then the 21 primitive rows over the native arguments.</summary>
    internal static string NativeMatrix() => Matrix(Natives, [.. Primitives, .. Natives]) + Matrix(Primitives, Natives);

    internal static string EnumMatrix() => Matrix(EnumsAndNumbers.Where(static t => t.IsEnum), EnumsAndNumbers)
        + Matrix(EnumsAndNumbers.Where(static t => !t.IsEnum), EnumsAndNumbers);

    internal static string NullableMatrix() =>
        Matrix([typeof(int?), typeof(long?), typeof(decimal?)], Nullables);

    private static string Matrix(IEnumerable<Type> parameters, IEnumerable<Type> arguments) {
        var arguments_ = arguments.ToArray();
        var text = new StringBuilder();
        text.Append("args:").Append(string.Join(',', arguments_.Select(ParameterName))).Append('\n');
        foreach (var parameter in parameters)
            text.Append(ParameterName(parameter)).Append(' ').Append(Row(ParameterName(parameter), arguments_)).Append('\n');
        return text.ToString();
    }

    private static string Signature(ITypeMethod method) =>
        $"{method.DeclaringTypeDefinition.Name}.{method.Name}({string.Join(',', method.Parameters.Select(static p => p.ParameterTypeDefinition.Name))})";

    internal static string Winners(Type type, string name, params Type[] arguments) {
        var winners = Clr(type).FindMatchingMethodOverloads(name, arguments.Select(Clr)).Select(Signature).Order(StringComparer.Ordinal);
        var rendered = string.Join(" | ", winners);
        return $"{type.Name}.{name}({string.Join(',', arguments.Select(static a => a.Name))}) = {(rendered.Length == 0 ? "none" : rendered)}";
    }

    internal static string WinnerTable() {
        var text = new StringBuilder();
        void Add(Type type, string name, params Type[] arguments) => text.Append(Winners(type, name, arguments)).Append('\n');
        // The three pins that differ from C# 12.6.4.
        Add(typeof(Math), "Round", typeof(long));
        Add(typeof(Math), "Max", typeof(long), typeof(ulong));
        Add(typeof(Overloads), "ObjectVsLong", typeof(int));
        // Tier 1 (a declared operator) against tier 2 (a built-in conversion).
        Add(typeof(Overloads), "LongVsDecimal", typeof(int));
        Add(typeof(Overloads), "LongVsDouble", typeof(int));
        Add(typeof(Overloads), "MetersVsDecimal", typeof(double));
        Add(typeof(Overloads), "MetersVsDecimal", typeof(long));
        // Generated and library call sets.
        Add(typeof(Math), "Max", typeof(long), typeof(decimal));
        Add(typeof(Math), "Max", typeof(int), typeof(long));
        Add(typeof(Math), "Max", typeof(char), typeof(int));
        Add(typeof(Math), "Round", typeof(int));
        Add(typeof(Math), "Round", typeof(double));
        Add(typeof(Math), "Truncate", typeof(double));
        Add(typeof(Math), "Truncate", typeof(decimal));
        Add(typeof(Math), "Abs", typeof(sbyte));
        Add(typeof(Math), "Abs", typeof(long));
        Add(typeof(DateTime), "AddDays", typeof(long));
        Add(typeof(DateTime), "AddDays", typeof(int));
        Add(typeof(DateTime), "AddMonths", typeof(int));
        Add(typeof(DateTime), "AddMonths", typeof(long));
        Add(typeof(DateOnly), "AddDays", typeof(int));
        Add(typeof(DateOnly), "AddDays", typeof(long));
        Add(typeof(TimeOnly), "AddHours", typeof(long));
        Add(typeof(TimeSpan), "FromSeconds", typeof(long));
        Add(typeof(TimeSpan), "FromSeconds", typeof(int));
        Add(typeof(TimeSpan), "FromDays", typeof(long));
        Add(typeof(TimeSpan), "FromMilliseconds", typeof(long));
        Add(typeof(string), "Concat", typeof(string), typeof(string));
        Add(typeof(string), "Concat", typeof(string), typeof(object));
        Add(typeof(string), "Concat", typeof(object), typeof(object));
        Add(typeof(string), "IsNullOrEmpty", typeof(string));
        Add(typeof(Convert), "ToDecimal", typeof(decimal));
        Add(typeof(Convert), "ToDecimal", typeof(long));
        Add(typeof(Convert), "ToDecimal", typeof(double));
        Add(typeof(Convert), "ToInt64", typeof(long));
        Add(typeof(Convert), "ToInt64", typeof(decimal));
        Add(typeof(Convert), "ToDouble", typeof(double));
        Add(typeof(Convert), "ToDouble", typeof(long));
        Add(typeof(decimal), "Add", typeof(decimal), typeof(decimal));
        Add(typeof(decimal), "Add", typeof(long), typeof(long));
        return text.ToString();
    }

    // ---- captured from origin/master ----

    private const string MasterPrimitiveMatrix = """
        args:Boolean,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,String,Char,DateTime,DateOnly,TimeOnly,TimeSpan,Guid,ByteArray,Structure
        Boolean A....................
        Int8 .A...A...............
        Int16 .AA..AA......A.......
        Int32 .AAA.AAA.....A.......
        Int64 .AAAAAAAA....A.......
        UInt8 .A...A...............
        UInt16 .AA..AA......A.......
        UInt32 .AAA.AAA.....A.......
        UInt64 .AAAAAAAA....A.......
        Float32 .AAAAAAAAA...A.......
        Float64 .AAAAAAAAAA..A.......
        Decimal .AAAAAAAAAAA.A.......
        String ............A........
        Char .AA..AA......A.......
        DateTime ..............A......
        DateOnly ...............A.....
        TimeOnly ................A....
        TimeSpan .................A...
        Guid ..................A..
        ByteArray ...................A.
        Structure AAAAAAAAAAAAAAAAAAAAA
        """;
    private const string MasterEnumMatrix = """
        args:Color,Shape,Low,Big,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,Char
        Color AAA.AAA.AAA....A
        Shape AAA.AAA.AAA....A
        Low ..A.A...A.......
        Big AAAAAAAAAAAA...A
        args:Color,Shape,Low,Big,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,Char
        Int8 ..A.A...A.......
        Int16 ..A.AA..AA.....A
        Int32 AAA.AAA.AAA....A
        Int64 AAAAAAAAAAAA...A
        UInt8 ..A.A...A.......
        UInt16 ..A.AA..AA.....A
        UInt32 AAA.AAA.AAA....A
        UInt64 AAAAAAAAAAAA...A
        Float32 AAAAAAAAAAAAA..A
        Float64 AAAAAAAAAAAAAA.A
        Decimal AAAAAAAAAAAAAAAA
        Char ..A.AA..AA.....A
        """;
    private const string MasterNullableMatrix = """
        args:Int32,Int64,Decimal,Float64,NullableInt32,NullableInt64,NullableDecimal
        NullableInt32 A...A..
        NullableInt64 .A...A.
        NullableDecimal ..A...A
        """;
    private const string MasterWinners = """
        Math.Round(Int64) = Math.Round(Decimal)
        Math.Max(Int64,UInt64) = Math.Max(Decimal,Decimal) | Math.Max(Int64,Int64) | Math.Max(UInt64,UInt64)
        Overloads.ObjectVsLong(Int32) = Overloads.ObjectVsLong(Object)
        Overloads.LongVsDecimal(Int32) = Overloads.LongVsDecimal(Decimal)
        Overloads.LongVsDouble(Int32) = Overloads.LongVsDouble(Double) | Overloads.LongVsDouble(Int64)
        Overloads.MetersVsDecimal(Double) = Overloads.MetersVsDecimal(Meters)
        Overloads.MetersVsDecimal(Int64) = Overloads.MetersVsDecimal(Decimal)
        Math.Max(Int64,Decimal) = Math.Max(Decimal,Decimal)
        Math.Max(Int32,Int64) = Math.Max(Decimal,Decimal) | Math.Max(Int64,Int64)
        Math.Max(Char,Int32) = Math.Max(Decimal,Decimal) | Math.Max(Int32,Int32)
        Math.Round(Int32) = Math.Round(Decimal)
        Math.Round(Double) = Math.Round(Double)
        Math.Truncate(Double) = Math.Truncate(Double)
        Math.Truncate(Decimal) = Math.Truncate(Decimal)
        Math.Abs(SByte) = Math.Abs(SByte)
        Math.Abs(Int64) = Math.Abs(Int64)
        DateTime.AddDays(Int64) = DateTime.AddDays(Double)
        DateTime.AddDays(Int32) = DateTime.AddDays(Double)
        DateTime.AddMonths(Int32) = DateTime.AddMonths(Int32)
        DateTime.AddMonths(Int64) = none
        DateOnly.AddDays(Int32) = DateOnly.AddDays(Int32)
        DateOnly.AddDays(Int64) = none
        TimeOnly.AddHours(Int64) = TimeOnly.AddHours(Double)
        TimeSpan.FromSeconds(Int64) = TimeSpan.FromSeconds(Int64) | TimeSpan.FromSeconds(Int64,Int64,Int64)
        TimeSpan.FromSeconds(Int32) = TimeSpan.FromSeconds(Double) | TimeSpan.FromSeconds(Int64) | TimeSpan.FromSeconds(Int64,Int64,Int64)
        TimeSpan.FromDays(Int64) = TimeSpan.FromDays(Double)
        TimeSpan.FromMilliseconds(Int64) = TimeSpan.FromMilliseconds(Int64)
        String.Concat(String,String) = String.Concat(String,String)
        String.Concat(String,Object) = String.Concat(Object,Object)
        String.Concat(Object,Object) = String.Concat(Object,Object)
        String.IsNullOrEmpty(String) = String.IsNullOrEmpty(String)
        Convert.ToDecimal(Decimal) = Convert.ToDecimal(Decimal)
        Convert.ToDecimal(Int64) = Convert.ToDecimal(Int64)
        Convert.ToDecimal(Double) = Convert.ToDecimal(Double)
        Convert.ToInt64(Int64) = Convert.ToInt64(Int64)
        Convert.ToInt64(Decimal) = Convert.ToInt64(Decimal)
        Convert.ToDouble(Double) = Convert.ToDouble(Double)
        Convert.ToDouble(Int64) = Convert.ToDouble(Int64)
        Decimal.Add(Decimal,Decimal) = Decimal.Add(Decimal,Decimal)
        Decimal.Add(Int64,Int64) = Decimal.Add(Decimal,Decimal)
        """;

    private static readonly MethodInfo ArgumentMatchesMethod =
        typeof(SyntaxTypeCompatibilityAnalyzer).GetMethod("ArgumentMatches", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo TryConversionInvokeMethod =
        typeof(SyntaxTypeCompatibilityAnalyzer).GetMethod("TryConversionInvoke", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The analyzer's own plausibility check (<c>ArgumentMatches</c>) for the same lone methods.</summary>
    private static string AnalyzerAccepts(string parameterMethod, Type argument) {
        var parameter = Clr(typeof(Lone)).Methods.WithName(parameterMethod).Single().Parameters.Single();
        return (bool)ArgumentMatchesMethod.Invoke(null, [AnalysisContext.CreateDefault(), parameter, Clr(argument), Wrap(1)])! ? "A" : ".";
    }

    internal static string AnalyzerMatrix() {
        var text = new StringBuilder();
        text.Append(Matrix(Primitives, Primitives, AnalyzerAccepts));
        text.Append(Matrix(EnumsAndNumbers.Where(static t => t.IsEnum), EnumsAndNumbers, AnalyzerAccepts));
        text.Append(Matrix(EnumsAndNumbers.Where(static t => !t.IsEnum), EnumsAndNumbers, AnalyzerAccepts));
        text.Append(Matrix([typeof(int?), typeof(long?), typeof(decimal?)], Nullables, AnalyzerAccepts));
        return text.ToString();
    }

    private static string TryConversion(ITypeDefinition destination, ITypeDefinition source, bool implicitOnly) {
        var result = TryConversionInvokeMethod.Invoke(null, [AnalysisContext.CreateDefault(), Wrap(1), destination, source, implicitOnly]);
        return result is Invoke { Delegate: Member member } ? $"Invoke({member.MemberName})" : result is null ? "null" : result.GetType().Name;
    }

    internal static string GuardTable() {
        var set = new ModeledTypeSet();
        ITypeDefinition bag = set.Add("Bag");
        ITypeDefinition bags = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        ITypeDefinition freshBags = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        ITypeDefinition otherBags = new AstCollectionTypeDefinition(set.Add("Crate"), CollectionKind.List);
        var text = new StringBuilder();
        void Add(string label, ITypeDefinition destination, ITypeDefinition source, bool implicitOnly = true) =>
            text.Append(label).Append(implicitOnly ? " (implicit)" : " (explicit)").Append(" = ").Append(TryConversion(destination, source, implicitOnly)).Append('\n');
        Add("decimal? <- decimal", Clr(typeof(decimal?)), Clr(typeof(decimal)));
        Add("decimal? <- decimal", Clr(typeof(decimal?)), Clr(typeof(decimal)), implicitOnly: false);
        Add("object <- decimal", Clr(typeof(object)), Clr(typeof(decimal)));
        Add("object <- Bag", Clr(typeof(object)), bag);
        Add("Bag[] <- Bag[] (fresh)", bags, freshBags);
        Add("Bag[] <- Crate[]", bags, otherBags);
        Add("Meters <- double", Clr(typeof(Meters)), Clr(typeof(double)));
        Add("decimal <- long", Clr(typeof(decimal)), Clr(typeof(long)));
        Add("long <- int", Clr(typeof(long)), Clr(typeof(int)));
        Add("int <- long", Clr(typeof(int)), Clr(typeof(long)), implicitOnly: false);
        Add("double <- Meters", Clr(typeof(double)), Clr(typeof(Meters)));
        Add("double <- Meters", Clr(typeof(double)), Clr(typeof(Meters)), implicitOnly: false);
        Add("decimal <- double", Clr(typeof(decimal)), Clr(typeof(double)));
        Add("decimal <- double", Clr(typeof(decimal)), Clr(typeof(double)), implicitOnly: false);
        return text.ToString();
    }

    private static string Matrix(IEnumerable<Type> parameters, IEnumerable<Type> arguments, Func<string, Type, string> accepts) {
        var argumentList = arguments.ToArray();
        var text = new StringBuilder();
        text.Append("args:").Append(string.Join(',', argumentList.Select(ParameterName))).Append('\n');
        foreach (var parameter in parameters)
            text.Append(ParameterName(parameter)).Append(' ')
                .Append(string.Concat(argumentList.Select(argument => accepts(ParameterName(parameter), argument)))).Append('\n');
        return text.ToString();
    }

    private static string Trim(string text) => text.TrimEnd('\n');
    private const string MasterAnalyzerMatrix = """
        args:Boolean,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,String,Char,DateTime,DateOnly,TimeOnly,TimeSpan,Guid,ByteArray,Structure
        Boolean A....................
        Int8 .A...A...............
        Int16 .AA..AA......A.......
        Int32 .AAA.AAA.....A.......
        Int64 .AAAAAAAA....A.......
        UInt8 .A...A...............
        UInt16 .AA..AA......A.......
        UInt32 .AAA.AAA.....A.......
        UInt64 .AAAAAAAA....A.......
        Float32 .AAAAAAAAA...A.......
        Float64 .AAAAAAAAAA..A.......
        Decimal .AAAAAAAAAAA.A.......
        String ............A........
        Char .AA..AA......A.......
        DateTime ..............A......
        DateOnly ...............A.....
        TimeOnly ................A....
        TimeSpan .................A...
        Guid ..................A..
        ByteArray ...................A.
        Structure AAAAAAAAAAAAAAAAAAAAA
        args:Color,Shape,Low,Big,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,Char
        Color AAA.AAA.AAA....A
        Shape AAA.AAA.AAA....A
        Low ..A.A...A.......
        Big AAAAAAAAAAAA...A
        args:Color,Shape,Low,Big,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,Char
        Int8 ..A.A...A.......
        Int16 ..A.AA..AA.....A
        Int32 AAA.AAA.AAA....A
        Int64 AAAAAAAAAAAA...A
        UInt8 ..A.A...A.......
        UInt16 ..A.AA..AA.....A
        UInt32 AAA.AAA.AAA....A
        UInt64 AAAAAAAAAAAA...A
        Float32 AAAAAAAAAAAAA..A
        Float64 AAAAAAAAAAAAAA.A
        Decimal AAAAAAAAAAAAAAAA
        Char ..A.AA..AA.....A
        args:Int32,Int64,Decimal,Float64,NullableInt32,NullableInt64,NullableDecimal
        NullableInt32 A...A..
        NullableInt64 .A...A.
        NullableDecimal ..A...A
        """;
    private const string MasterGuardTable = """
        decimal? <- decimal (implicit) = null
        decimal? <- decimal (explicit) = null
        object <- decimal (implicit) = null
        object <- Bag (implicit) = null
        Bag[] <- Bag[] (fresh) (implicit) = null
        Bag[] <- Crate[] (implicit) = null
        Meters <- double (implicit) = Invoke(op_Implicit)
        decimal <- long (implicit) = Invoke(op_Implicit)
        long <- int (implicit) = null
        int <- long (explicit) = null
        double <- Meters (implicit) = null
        double <- Meters (explicit) = Invoke(op_Explicit)
        decimal <- double (implicit) = null
        decimal <- double (explicit) = Invoke(op_Explicit)
        """;

    private static readonly string[] EnumNames = ["Color", "Shape", "Low", "Big"];

    /// <summary>
    /// The closed delta, applied to a matrix captured from master: the 21 widenings the rank table invented are gone
    /// (<see cref="TypeRulesTests.RemovedWidenings"/>), and an enum accepts only itself among enums and numbers.
    /// Everything else stays, cell for cell.
    /// </summary>
    internal static string AfterT16(string masterMatrix) {
        var removed = TypeRulesTests.RemovedWidenings.Select(static pair => (From: pair.From.ToString(), To: pair.To.ToString())).ToHashSet();
        var arguments = Array.Empty<string>();
        var output = new List<string>();
        foreach (var line in masterMatrix.Split('\n')) {
            if (line.StartsWith("args:", StringComparison.Ordinal)) {
                arguments = line["args:".Length..].Split(',');
                output.Add(line);
                continue;
            }
            var space = line.IndexOf(' ');
            var parameter = line[..space];
            var cells = line[(space + 1)..].ToCharArray();
            for (var i = 0; i < cells.Length; i++) {
                var enumPair = (EnumNames.Contains(parameter) || EnumNames.Contains(arguments[i])) && parameter != arguments[i];
                if (enumPair || removed.Contains((arguments[i], parameter)))
                    cells[i] = '.';
            }
            output.Add($"{parameter} {new string(cells)}");
        }
        return string.Join('\n', output);
    }

    [Test]
    public async Task Acceptance_EveryPrimitivePair_IsMasterMinusTheRemovedWidenings() =>
        await Assert.That(Trim(PrimitiveMatrix())).IsEqualTo(AfterT16(MasterPrimitiveMatrix));

    /// <summary>
    /// T-18b: a native-integer parameter accepts its implicit sources (C# 11: sbyte, byte, short, ushort, int and char
    /// into nint; byte, ushort, char and uint into nuint) and itself; a native argument reaches long or ulong, float,
    /// double, decimal and object. Written from the specification, and checked against Roslyn below.
    /// </summary>
    private const string NativeIntegerMatrix = """
        args:Boolean,Int8,Int16,Int32,Int64,UInt8,UInt16,UInt32,UInt64,Float32,Float64,Decimal,String,Char,DateTime,DateOnly,TimeOnly,TimeSpan,Guid,ByteArray,Structure,NInt,NUInt
        NInt .AAA.AA......A.......A.
        NUInt .....AAA.....A........A
        args:NInt,NUInt
        Boolean ..
        Int8 ..
        Int16 ..
        Int32 ..
        Int64 A.
        UInt8 ..
        UInt16 ..
        UInt32 ..
        UInt64 .A
        Float32 AA
        Float64 AA
        Decimal AA
        String ..
        Char ..
        DateTime ..
        DateOnly ..
        TimeOnly ..
        TimeSpan ..
        Guid ..
        ByteArray ..
        Structure AA
        """;

    [Test]
    public async Task Acceptance_NativeIntegerRowsAndColumns_ArePinnedApart_AndAgreeWithCSharp() {
        await Assert.That(Trim(NativeMatrix())).IsEqualTo(NativeIntegerMatrix);
        // The numeric cells against Roslyn: a parameter accepts an argument when C# converts it implicitly.
        foreach (var (from, to) in CSharpOperatorOracle.ConversionUniverse()) {
            if (!Primitives.Concat(Natives).Contains(from) || !Primitives.Concat(Natives).Contains(to))
                continue;
            var accepts = Accepts(ParameterName(to), from) == "A";
            await Assert.That(accepts).IsEqualTo(CSharpOperatorOracle.Conversion(from, to) is ConversionOperatorKind.Implicit)
                .Because($"{ParameterName(to)} <- {ParameterName(from)}");
        }
    }

    [Test]
    public async Task Acceptance_EnumAndNumericPairs_AreMasterMinusEveryEnumConversion() =>
        await Assert.That(Trim(EnumMatrix())).IsEqualTo(AfterT16(MasterEnumMatrix));

    [Test]
    public async Task Acceptance_NullableLift_IsMasterPlusTheLiftedImplicitPairs() =>
        await Assert.That(Trim(NullableMatrix())).IsEqualTo(AfterT21c(AfterT16(MasterNullableMatrix)));

    [Test]
    public async Task Delta_IsExactly_TheRemovedWideningsAndTheEnumPairs() {
        var changed = 0;
        foreach (var (before, after) in new[] { MasterPrimitiveMatrix, MasterEnumMatrix, MasterNullableMatrix }.Zip(
                     new[] { AfterT16(MasterPrimitiveMatrix), AfterT16(MasterEnumMatrix), AfterT16(MasterNullableMatrix) }))
            changed += before.Zip(after).Count(static pair => pair.First != pair.Second);
        // 21 removed widenings in the primitive matrix; in the enum matrices the numeric block loses its 21 pairs too
        // (counted below) plus every enum <-> different enum or number cell.
        await Assert.That(TypeRulesTests.RemovedWidenings.Length).IsEqualTo(21);
        await Assert.That(changed).IsGreaterThan(21);
    }

    [Test]
    public async Task Winners_AreAsMasterBinds_ExceptTheOneTier2TieThatLostACandidate() =>
        await Assert.That(Trim(WinnerTable())).IsEqualTo(MasterWinners.Replace(
            "Math.Max(Int64,UInt64) = Math.Max(Decimal,Decimal) | Math.Max(Int64,Int64) | Math.Max(UInt64,UInt64)",
            "Math.Max(Int64,UInt64) = Math.Max(Decimal,Decimal)"));

    [Test]
    public async Task AnalyzerArgumentMatches_AgreesWithTheScorer_OnEveryPair() {
        await Assert.That(Trim(AnalyzerMatrix())).IsEqualTo(AfterT21c(AfterT16(MasterAnalyzerMatrix)));
        await Assert.That(Trim(AnalyzerMatrix())).IsEqualTo(
            AfterT16(MasterPrimitiveMatrix) + "\n" + AfterT16(MasterEnumMatrix) + "\n" + AfterT21c(AfterT16(MasterNullableMatrix)));
    }

    [Test]
    public async Task TryConversionInvoke_Guard_IsAsMasterAnswers() =>
        await Assert.That(Trim(GuardTable())).IsEqualTo(MasterGuardTable);

    [Test]
    public async Task AnalyzerArgumentMatches_StringIntoEnumParameter_StaysAccepted() {
        var enumParameter = Clr(typeof(Lone)).Methods.WithName("Color").Single().Parameters.Single();
        await Assert.That((bool)ArgumentMatchesMethod.Invoke(null,
            [AnalysisContext.CreateDefault(), enumParameter, Clr(typeof(string)), Wrap("Red")])!).IsTrue();
        await Assert.That(AnalyzerAccepts("Color", typeof(bool))).IsEqualTo(".");
    }

    // ---- the three winners that differ from C# 12.6.4 (better function member) ----

    [Test]
    public async Task Winner_MathRoundInt64_BindsDecimal_DiffersFromCSharp_DeliberateUntilABetterConversionRowExists() {
        // C# 12.6.4 reports CS0121 (ambiguous: decimal and double have no better conversion between them).
        // Poly: a declared operator (long -> decimal) is tier 1, the built-in long -> double is tier 2.
        await Assert.That(Winners(typeof(Math), "Round", typeof(long))).IsEqualTo("Math.Round(Int64) = Math.Round(Decimal)");
    }

    [Test]
    public async Task Winner_MathMaxInt64UInt64_IsDecimal_DiffersFromCSharp_DeliberateUntilABetterConversionRowExists() {
        // C# 12.6.4 reports CS0121 (decimal, double and float are all applicable and none is better). Poly keeps every
        // candidate of the lowest score and the resolution pass takes the first. On master three candidates tied at 2
        // because the rank table called long -> ulong and ulong -> long widening; the provider says neither converts, so
        // decimal (two declared operators, 1 + 1) is the only candidate left at 2. API-only: the DSL has no such call.
        await Assert.That(Winners(typeof(Math), "Max", typeof(long), typeof(ulong)))
            .IsEqualTo("Math.Max(Int64,UInt64) = Math.Max(Decimal,Decimal)");
    }

    [Test]
    public async Task Winner_ObjectVersusLongWithInt32_BindsObject_DiffersFromCSharp_DeliberateUntilABetterConversionRowExists() {
        // C# 12.6.4 picks long (the implicit numeric conversion is better than boxing). Poly: assignable to object is tier 1.
        await Assert.That(Winners(typeof(Overloads), "ObjectVsLong", typeof(int)))
            .IsEqualTo("Overloads.ObjectVsLong(Int32) = Overloads.ObjectVsLong(Object)");
    }

    // ---- the tier 1 / tier 2 boundary is "the conversion states a method" (amendment 8) ----

    [Test]
    public async Task TierBoundary_DeclaredOperatorBeatsABuiltInConversion_ForTheSameArgument() {
        // decimal <- int goes through the declared op_Implicit (tier 1); long <- int is applied by the platform (tier 2).
        await Assert.That(Winners(typeof(Overloads), "LongVsDecimal", typeof(int)))
            .IsEqualTo("Overloads.LongVsDecimal(Int32) = Overloads.LongVsDecimal(Decimal)");
        var decimalFromInt = Clr(typeof(decimal)).ResolveConversionFrom(Clr(typeof(int)));
        var longFromInt = Clr(typeof(long)).ResolveConversionFrom(Clr(typeof(int)));
        await Assert.That(decimalFromInt!.Value.Method).IsNotNull();
        await Assert.That(longFromInt!.Value.Method).IsNull();
        await Assert.That(Clr(typeof(decimal)).IsAssignableFrom(Clr(typeof(int)))).IsTrue();
        await Assert.That(Clr(typeof(long)).IsAssignableFrom(Clr(typeof(int)))).IsFalse();
    }

    [Test]
    public async Task TierBoundary_EveryImplicitConversionWithAMethod_IsAssignable_AndEveryOneWithout_IsNot() {
        var mismatches = new List<string>();
        foreach (var to in Primitives.Concat(Natives).Where(static t => t != typeof(object))) {
            foreach (var from in Primitives.Concat(Natives)) {
                var conversion = Clr(to).ResolveConversionFrom(Clr(from));
                if (conversion is not { Kind: ConversionOperatorKind.Implicit } implicitConversion)
                    continue;
                var assignable = Clr(to).IsAssignableFrom(Clr(from));
                if (assignable != (implicitConversion.Method is not null))
                    mismatches.Add($"{from.Name} -> {to.Name}: method={implicitConversion.Method is not null} assignable={assignable}");
            }
        }
        await Assert.That(mismatches).IsEmpty();
    }

    // ---- the boxing rule is stated twice, and the two statements agree (amendment 4) ----

    [Test]
    public async Task ObjectAcceptsEveryClrDefinition_IsAssignableFromAndResolveConversionFromAgree() {
        var objectType = Clr(typeof(object));
        var types = typeof(object).Assembly.GetExportedTypes()
            .Where(static t => !t.IsGenericTypeDefinition && !t.IsPointer && !t.IsByRef && t != typeof(object))
            .Concat(Primitives.Where(static t => t != typeof(object)))
            .Concat([typeof(int?), typeof(int[]), typeof(List<int>), typeof(IDisposable), typeof(Action), typeof(ColorEnum), typeof(Meters)])
            .Distinct()
            .ToArray();
        var disagreements = new List<string>();
        foreach (var type in types) {
            var definition = Clr(type);
            var assignable = objectType.IsAssignableFrom(definition);
            var converts = objectType.ResolveConversionFrom(definition) is { Kind: ConversionOperatorKind.Implicit };
            if (!assignable || !converts)
                disagreements.Add($"{type.FullName}: IsAssignableFrom={assignable} ResolveConversionFrom(implicit)={converts}");
        }
        await Assert.That(types.Length).IsGreaterThan(500);
        await Assert.That(disagreements).IsEmpty();
        await Assert.That(objectType.IsAssignableFrom(objectType)).IsTrue();
        await Assert.That(objectType.ResolveConversionFrom(objectType)).IsNull();
    }

    [Test]
    public async Task ObjectAcceptsAModeledDefinition_ByTheBoxingRule_ButHasNoConversionFromIt() {
        // The VM boxes a modeled definition as object (DomainResult.Success). That is assignability only: the conversion
        // answer stays none, so the two statements of the boxing rule agree on every CLR definition and differ here by design.
        var objectType = Clr(typeof(object));
        ITypeDefinition modeled = new ModeledTypeSet().Add("Bag");
        await Assert.That(objectType.IsAssignableFrom(modeled)).IsTrue();
        await Assert.That(objectType.ResolveConversionFrom(modeled)).IsNull();
    }

    // ---- a declared operator is tier 1, and one step only ----

    [Test]
    public async Task UserDefinedImplicit_IsAcceptedAsOneStep_AndNotChained() {
        await Assert.That(Accepts("Metres", typeof(double))).IsEqualTo("A");
        await Assert.That(Accepts("Metres", typeof(int))).IsEqualTo(".");
        await Assert.That(Accepts("Decimal", typeof(Meters))).IsEqualTo(".");
        await Assert.That(Accepts("Float64", typeof(Meters))).IsEqualTo(".");
    }

    // ---- fail closed: a lone candidate that needed a removed widening or an enum conversion now has no overload ----

    public sealed class Receiver {
        public string TakesSByte(sbyte x) => "sbyte";
        public string TakesByte(byte x) => "byte";
        public string TakesChar(char x) => "char";
        public string TakesDecimal(decimal x) => "decimal";
        public string TakesDouble(double x) => "double";
        public string TakesInt(int x) => "int";
        public string TakesColor(ColorEnum x) => "color";
    }

    private static string NoMember(string method) => $"no matching member '{method}' on 'Receiver' for invoke with 1 argument(s)";

    private static string[] ErrorsOf(string method, object argument) {
        var invoke = new Invoke(new Member(Wrap(new Receiver()), method), Wrap(argument));
        return [.. Interpreter.Analyze(invoke).Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message)];
    }

    [Test]
    [Arguments("TakesByte", (sbyte)-1, "signed to its unsigned sibling")]
    [Arguments("TakesSByte", (byte)1, "unsigned to its signed sibling")]
    [Arguments("TakesChar", (short)1, "a number into Char")]
    [Arguments("TakesDecimal", 1.5, "Float64 into Decimal (explicit)")]
    [Arguments("TakesInt", ColorEnum.Red, "an enum into a number (explicit)")]
    [Arguments("TakesColor", 0, "a number into an enum (explicit)")]
    public async Task LoneCandidate_NeedingARemovedConversion_HasNoOverload(string method, object argument, string why) {
        await Assert.That(ErrorsOf(method, argument)).IsEquivalentTo([NoMember(method)]).Because(why);
    }

    [Test]
    [Arguments("TakesByte", (byte)1)]
    [Arguments("TakesSByte", (sbyte)1)]
    [Arguments("TakesChar", 'c')]
    [Arguments("TakesDecimal", 12L)]
    [Arguments("TakesDouble", 1.5)]
    [Arguments("TakesInt", 12)]
    [Arguments("TakesColor", ColorEnum.Red)]
    public async Task LoneCandidate_ThatTheSpecificationAccepts_StillBinds(string method, object argument) =>
        await Assert.That(ErrorsOf(method, argument)).IsEmpty();

    // ---- row (a): a fresh synthetic collection is the same type, so it scores 0, not 1 ----

    [Test]
    public async Task SyntheticCollectionParameter_ScoresAsTheSameType_AndBeatsADeclaredConversionTie() {
        var set = new ModeledTypeSet();
        var list = new CollectionTypeReference(new NamedTypeReference("Bag"), CollectionKind.List);
        set.Add("Bag", new MethodDefinitionNode("op_Implicit", new NamedTypeReference("Bag"), [new Parameter("a", list)], IsStatic: true));
        var host = (ITypeDefinition)set.Add("Host",
            new MethodDefinitionNode("Take", new NamedTypeReference("Bag"), [new Parameter("a", list)]),
            new MethodDefinitionNode("Take", new NamedTypeReference("Bag"), [new Parameter("a", new NamedTypeReference("Bag"))]));
        ITypeDefinition fresh = new AstCollectionTypeDefinition((ITypeDefinition)set.GetTypeDefinition("Bag")!, CollectionKind.List);
        var winners = host.FindMatchingMethodOverloads("Take", [fresh]).ToArray();
        await Assert.That(winners.Length).IsEqualTo(1);
        await Assert.That(winners[0].Parameters.Single().ParameterTypeDefinition).IsTypeOf<AstCollectionTypeDefinition>();
        // The sibling: the single-element parameter alone still binds through the declared conversion.
        var bagOnly = (ITypeDefinition)set.Add("BagHost",
            new MethodDefinitionNode("Take", new NamedTypeReference("Bag"), [new Parameter("a", new NamedTypeReference("Bag"))]));
        await Assert.That(bagOnly.FindMatchingMethodOverloads("Take", [fresh]).Count()).IsEqualTo(1);
    }

    // ---- the one C# reader delta the sweep found ----

    [Test]
    public async Task CSharpIntLiteralIntoAnUnsignedParameter_HasNoOverload_ConstantConversionIsNotModeled() {
        // C# 10.2.11 converts the constant 1 to uint, so PopCount(1) binds PopCount(uint) in C#. Poly does not model
        // constant-expression conversions: master reached the same overload through its invented Int32 -> UInt32
        // widening, which also accepted a non-constant int (an error in C#). Found, not fixed (see the PR).
        var run = CSharpScript.Run("System.Numerics.BitOperations.PopCount(1)");
        await Assert.That(run.Success).IsFalse();
        await Assert.That(run.Diagnostics).Contains(static d => d.Contains("no matching member 'PopCount'", StringComparison.Ordinal));
        // The sibling that C# also accepts and that never used a removed pair: an exact argument binds.
        var exact = CSharpScript.Run("System.Numerics.BitOperations.PopCount((ulong)7)");
        await Assert.That(exact.Success).IsTrue();
        await Assert.That(exact.Value).IsEqualTo("3");
    }

    // ---- IsAssignableWithoutConversion: assignability minus every conversion ----

    [Test]
    public async Task IsAssignableWithoutConversion_ExcludesDeclaredOperators_AndBuiltInConversions() {
        // A declared operator is assignment (IsAssignableFrom) but not "without conversion".
        await Assert.That(Clr(typeof(Meters)).IsAssignableFrom(Clr(typeof(double)))).IsTrue();
        await Assert.That(Clr(typeof(Meters)).IsAssignableWithoutConversion(Clr(typeof(double)))).IsFalse();
        await Assert.That(Clr(typeof(decimal)).IsAssignableFrom(Clr(typeof(long)))).IsTrue();
        await Assert.That(Clr(typeof(decimal)).IsAssignableWithoutConversion(Clr(typeof(long)))).IsFalse();
        // A built-in numeric conversion is neither.
        await Assert.That(Clr(typeof(long)).IsAssignableFrom(Clr(typeof(int)))).IsFalse();
        await Assert.That(Clr(typeof(long)).IsAssignableWithoutConversion(Clr(typeof(int)))).IsFalse();
        // The siblings: identity, inheritance, interfaces, the T -> T? lift and the box into object need none.
        await Assert.That(Clr(typeof(long)).IsAssignableWithoutConversion(Clr(typeof(long)))).IsTrue();
        await Assert.That(Clr(typeof(IEnumerable<int>)).IsAssignableWithoutConversion(Clr(typeof(List<int>)))).IsTrue();
        await Assert.That(Clr(typeof(int?)).IsAssignableWithoutConversion(Clr(typeof(int)))).IsTrue();
        await Assert.That(Clr(typeof(int)).IsAssignableWithoutConversion(Clr(typeof(int?)))).IsFalse();
        await Assert.That(Clr(typeof(object)).IsAssignableWithoutConversion(Clr(typeof(decimal)))).IsTrue();
    }

    [Test]
    public async Task IsAssignableWithoutConversion_IsAlwaysInsideIsAssignableFrom_OnEveryPrimitivePair() {
        var escapes = new List<string>();
        foreach (var to in Primitives)
            foreach (var from in Primitives)
                if (Clr(to).IsAssignableWithoutConversion(Clr(from)) && !Clr(to).IsAssignableFrom(Clr(from)))
                    escapes.Add($"{from.Name} -> {to.Name}");
        await Assert.That(escapes).IsEmpty();
    }

    [Test]
    public async Task IsAssignableWithoutConversion_ModeledDefinitions_UseTheHierarchyAndIdentity() {
        var set = new ModeledTypeSet();
        ITypeDefinition bag = set.Add("Bag");
        ITypeDefinition crate = set.Add("Crate");
        ITypeDefinition bags = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        ITypeDefinition freshBags = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        await Assert.That(bag.IsAssignableWithoutConversion(bag)).IsTrue();
        await Assert.That(bag.IsAssignableWithoutConversion(crate)).IsFalse();
        await Assert.That(bags.IsAssignableWithoutConversion(freshBags)).IsTrue();
        await Assert.That(Clr(typeof(object)).IsAssignableWithoutConversion(bag)).IsTrue();
    }

    // ---- T-21c: C#'s implicit nullable conversion (10.6.1) is stated by the provider, with no method ----

    /// <summary>The cells T-21c moves, argument then parameter: each was refused on master and C# accepts it.</summary>
    private static readonly (string Argument, string Parameter)[] LiftedPairs = [
        ("Int32", "NullableInt64"), ("NullableInt32", "NullableInt64"),
        ("Int32", "NullableDecimal"), ("Int64", "NullableDecimal"),
        ("NullableInt32", "NullableDecimal"), ("NullableInt64", "NullableDecimal"),
    ];

    /// <summary>The closed T-21c delta, applied after <see cref="AfterT16"/>: the lifted implicit pairs accept. Nothing else moves.</summary>
    internal static string AfterT21c(string t16Matrix) {
        var arguments = Array.Empty<string>();
        var output = new List<string>();
        foreach (var line in t16Matrix.Split('\n')) {
            if (line.StartsWith("args:", StringComparison.Ordinal)) {
                arguments = line["args:".Length..].Split(',');
                output.Add(line);
                continue;
            }
            var space = line.IndexOf(' ');
            var parameter = line[..space];
            var cells = line[(space + 1)..].ToCharArray();
            for (var i = 0; i < cells.Length; i++)
                if (LiftedPairs.Contains((arguments[i], parameter)))
                    cells[i] = 'A';
            output.Add($"{parameter} {new string(cells)}");
        }
        return string.Join('\n', output);
    }

    private static readonly Type[] NullableParameters = [typeof(int?), typeof(long?), typeof(decimal?)];

    private static readonly Dictionary<string, Type> NullableUniverse = Nullables.ToDictionary(ParameterName);

    [Test]
    public async Task LiftedImplicit_IsStatedWithNoMethod_AndIsNotAssignment() {
        foreach (var (from, to) in new[] {
            (typeof(long), typeof(decimal?)), (typeof(int), typeof(long?)), (typeof(int), typeof(decimal?)),
            (typeof(int?), typeof(long?)), (typeof(long?), typeof(decimal?)), (typeof(int?), typeof(decimal?)),
        }) {
            var conversion = Clr(to).ResolveConversionFrom(Clr(from));
            await Assert.That(conversion is { Kind: ConversionOperatorKind.Implicit, Method: null }).IsTrue().Because($"{from.Name} -> {to.Name}");
            // Assignment, casts and type tests are not loosened: a method-less conversion is never assignability.
            await Assert.That(Clr(to).IsAssignableFrom(Clr(from))).IsFalse().Because($"{from.Name} -> {to.Name}");
            await Assert.That(Clr(to).IsAssignableWithoutConversion(Clr(from))).IsFalse().Because($"{from.Name} -> {to.Name}");
        }
        await Assert.That(Clr(typeof(decimal?)).IsAssignableFrom(Clr(typeof(long)))).IsFalse();
    }

    [Test]
    public async Task LiftedImplicit_IsOnlyForANullableTarget_AndOnlyWhereTheUnderlyingConversionIsImplicit() {
        // A non-nullable target never takes the lifted arm; a nullable source into a non-nullable target stays refused.
        await Assert.That(Clr(typeof(decimal)).ResolveConversionFrom(Clr(typeof(long?)))).IsNull();
        await Assert.That(Clr(typeof(long)).ResolveConversionFrom(Clr(typeof(int?)))).IsNull();
        // An explicit or absent underlying conversion lifts to nothing implicit.
        await Assert.That(Clr(typeof(decimal?)).ResolveConversionFrom(Clr(typeof(double)))?.Kind).IsNotEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(Clr(typeof(int?)).ResolveConversionFrom(Clr(typeof(long)))?.Kind).IsNotEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(Clr(typeof(long?)).ResolveConversionFrom(Clr(typeof(ulong)))?.Kind).IsNotEqualTo(ConversionOperatorKind.Implicit);
        // T -> T? keeps the method Nullable<T> declares (op_Implicit(T)): the lifted arm never replaces a declared conversion.
        await Assert.That(Clr(typeof(decimal?)).ResolveConversionFrom(Clr(typeof(decimal)))?.Method).IsNotNull();
        await Assert.That(Clr(typeof(decimal?)).ResolveConversionFrom(Clr(typeof(decimal?)))).IsNull();
    }

    [Test]
    public async Task Acceptance_EveryNullableCell_AgreesWithCSharp() {
        var disagreements = new List<string>();
        foreach (var parameter in NullableParameters)
            foreach (var argument in Nullables) {
                var accepts = Accepts(ParameterName(parameter), argument) == "A";
                var analyzer = AnalyzerAccepts(ParameterName(parameter), argument) == "A";
                var csharp = CSharpCallOracle.Binds(argument, parameter);
                if (accepts != csharp || analyzer != csharp)
                    disagreements.Add($"{ParameterName(parameter)} <- {ParameterName(argument)}: scorer={accepts} analyzer={analyzer} C#={csharp}");
            }
        await Assert.That(disagreements).IsEmpty();
    }

    [Test]
    public async Task LiftedDelta_IsExactlySixCells_EachRefusedOnMaster_AndAcceptedByCSharp() {
        var names = NullableUniverse;
        var before = AfterT16(MasterNullableMatrix).Split('\n');
        var after = AfterT21c(AfterT16(MasterNullableMatrix)).Split('\n');
        var arguments = before[0]["args:".Length..].Split(',');
        var moved = new List<(string Argument, string Parameter)>();
        for (var row = 1; row < before.Length; row++) {
            var parameter = before[row][..before[row].IndexOf(' ')];
            var (oldCells, newCells) = (before[row][(parameter.Length + 1)..], after[row][(parameter.Length + 1)..]);
            for (var i = 0; i < oldCells.Length; i++)
                if (oldCells[i] != newCells[i])
                    moved.Add((arguments[i], parameter));
        }
        await Assert.That(moved.Count).IsEqualTo(6);
        foreach (var (argument, parameter) in moved) {
            await Assert.That(CSharpCallOracle.Binds(names[argument], names[parameter])).IsTrue().Because($"{parameter} <- {argument}");
            await Assert.That(before.Single(line => line.StartsWith(parameter + " ", StringComparison.Ordinal))[(parameter.Length + 1 + Array.IndexOf(arguments, argument))]).IsEqualTo('.');
        }
    }

    /// <summary>The one consumer that moves beside overload binding: <c>GetCommonType</c> asks <c>ResolveConversionFrom</c>, so
    /// <c>{long?, int}</c> has a common type now (it had none). Each answer is C#'s type of <c>c ? a : b</c>.</summary>
    [Test]
    public async Task CommonType_OfALiftedPair_IsTheNullableType_AsCSharpTypesAConditional() {
        foreach (var (left, right, csharp, expected) in new (Type, Type, string, Type?)[] {
            (typeof(long?), typeof(int), "long?", typeof(long?)),
            (typeof(decimal?), typeof(long), "decimal?", typeof(decimal?)),
            (typeof(long?), typeof(int?), "long?", typeof(long?)),
            (typeof(long?), typeof(double), "none", null),
            (typeof(decimal?), typeof(double), "none", null),
            (typeof(int?), typeof(long), "none", null),
        }) {
            var common = new[] { Clr(left), Clr(right) }.GetCommonType()?.GetRuntimeType();
            await Assert.That(CSharpCallOracle.ConditionalType(left, right)).IsEqualTo(csharp).Because($"{left.Name} : {right.Name}");
            await Assert.That(common).IsEqualTo(expected).Because($"{left.Name} : {right.Name}");
        }
    }

    // ---- an overload tie the lifted arm creates: C# picks the better conversion target, the scorer keeps both ----

    public static class NullableOverloads {
        // decimal? first on purpose: a tie must not be won by declaration order unnoticed.
        public static string LongVsDecimal(decimal? x) => "decimal?";
        public static string LongVsDecimal(long? x) => "long?";
    }

    [Test]
    public async Task Winner_NullableLongVersusNullableDecimalWithInt32_ScorerReturnsBothCandidates_TieNotWinner_PinnedUntilRow847() {
        // C# 12.6.4: long? is the better conversion target (long? converts implicitly to decimal?, not back), so C# binds
        // M(long?). Both candidates are tier 2 here (a built-in lifted conversion, no method), so the scorer keeps both and the
        // resolution pass takes the first by declaration order. Overload tie-breaking is row #847 (owner ruling); the deviation is pinned.
        // This proves the TIE (both returned); the bound overload is measured end to end in ValueRepresentationEmitTests.OverloadTie_*.
        await Assert.That(CSharpCallOracle.Chosen([typeof(decimal?), typeof(long?)], typeof(int))).IsEqualTo("long?");
        var winners = Clr(typeof(NullableOverloads)).FindMatchingMethodOverloads("LongVsDecimal", [Clr(typeof(int))])
            .Select(static method => method.Parameters.Single().ParameterTypeDefinition.GetRuntimeType()!).ToArray();
        await Assert.That(winners).IsEquivalentTo(new[] { typeof(decimal?), typeof(long?) });
    }
}

/// <summary>Roslyn as the oracle for a call's binding: one tiny compilation per question, C# 12.</summary>
internal static class CSharpCallOracle {
    private static readonly Lazy<Microsoft.CodeAnalysis.MetadataReference[]> References = new(static () => [
        .. ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator)
            .Where(static path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Select(static path => (Microsoft.CodeAnalysis.MetadataReference)Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)),
        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(CSharpCallOracle).Assembly.Location),
    ]);

    public static string Spell(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? Spell(underlying) + "?" : "global::" + type.FullName!.Replace('+', '.');

    private static Microsoft.CodeAnalysis.Compilation Compile(string members) {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "class C {\n" + members + "\n}",
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12));
        return Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "CallOracle", [tree], References.Value,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
    }

    private static bool HasErrors(Microsoft.CodeAnalysis.Compilation compilation) =>
        compilation.GetDiagnostics().Any(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

    /// <summary>True when C# binds <c>Take(a)</c> for a variable <c>a</c> of <paramref name="argument"/> and a parameter of <paramref name="parameter"/>.</summary>
    public static bool Binds(Type argument, Type parameter) =>
        !HasErrors(Compile($"static void Take({Spell(parameter)} p) {{}}\nstatic void Call({Spell(argument)} a) => Take(a);"));

    /// <summary>True when C# binds <c>Take(expression)</c>, a literal or constant, for a parameter of <paramref name="parameter"/>.</summary>
    public static bool BindsExpression(string expression, Type parameter) =>
        !HasErrors(Compile($"static void Take({Spell(parameter)} p) {{}}\nstatic void Call() => Take({expression});"));

    /// <summary>C#'s type of <c>c ? a : b</c> for operands of the two types, or "none" when C# reports an error.</summary>
    public static string ConditionalType(Type left, Type right) {
        var compilation = Compile($"static void Call(bool c, {Spell(left)} a, {Spell(right)} b) {{ var r = c ? a : b; }}");
        if (HasErrors(compilation))
            return "none";
        var tree = compilation.SyntaxTrees.Single();
        var declarator = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>().Single();
        return Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(compilation.GetSemanticModel(tree), declarator) is Microsoft.CodeAnalysis.ILocalSymbol local
            ? local.Type.ToDisplayString()
            : "none";
    }

    /// <summary>The parameter type of the overload C# picks for <c>M(a)</c>, <c>a</c> of <paramref name="argument"/>.</summary>
    public static string Chosen(Type[] parameters, Type argument) {
        var members = string.Concat(parameters.Select(static p => $"static void M({Spell(p)} p) {{}}\n")) + $"static void Call({Spell(argument)} a) => M(a);";
        var compilation = Compile(members);
        if (HasErrors(compilation))
            return "error";
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Last();
        var method = (Microsoft.CodeAnalysis.IMethodSymbol)Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetSymbolInfo(compilation.GetSemanticModel(tree), invocation).Symbol!;
        return method.Parameters[0].Type.ToDisplayString();
    }
}