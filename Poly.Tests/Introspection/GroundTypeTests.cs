using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a ground type is, the definition answers (#998): its number representation, text, truth, time. No shared list of
/// primitive ids stands between a platform and a consumer.
/// </summary>
public class GroundTypeTests {
    private static ITypeDefinition Clr<T>() => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(T));

    private static (bool IsNumber, bool IsIntegral, bool IsSigned, int NumberBits, bool IsDecimal, bool IsNativeSized) Number(ITypeDefinition t) =>
        (t.IsNumber, t.IsIntegral, t.IsSigned, t.NumberBits, t.IsDecimal, t.IsNativeSized);

    private static (bool IsTemporal, bool HasDate, bool HasTime, bool HasOffset, bool IsDuration) Time(ITypeDefinition t) =>
        (t.IsTemporal, t.HasDate, t.HasTime, t.HasOffset, t.IsDuration);

    [Test]
    public async Task ClrNumbers_StateTheirRepresentation() {
        await Assert.That(Number(Clr<sbyte>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: true, NumberBits: 8, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<short>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: true, NumberBits: 16, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<int>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: true, NumberBits: 32, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<long>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: true, NumberBits: 64, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<byte>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: false, NumberBits: 8, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<ushort>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: false, NumberBits: 16, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<uint>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: false, NumberBits: 32, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<ulong>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: false, NumberBits: 64, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<float>())).IsEqualTo((IsNumber: true, IsIntegral: false, IsSigned: true, NumberBits: 32, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<double>())).IsEqualTo((IsNumber: true, IsIntegral: false, IsSigned: true, NumberBits: 64, IsDecimal: false, IsNativeSized: false));
        await Assert.That(Number(Clr<decimal>())).IsEqualTo((IsNumber: true, IsIntegral: false, IsSigned: true, NumberBits: 128, IsDecimal: true, IsNativeSized: false));
        await Assert.That(Number(Clr<nint>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: true, NumberBits: IntPtr.Size * 8, IsDecimal: false, IsNativeSized: true));
        await Assert.That(Number(Clr<nuint>())).IsEqualTo((IsNumber: true, IsIntegral: true, IsSigned: false, NumberBits: IntPtr.Size * 8, IsDecimal: false, IsNativeSized: true));
    }

    [Test]
    public async Task ClrNonNumbers_StateNoNumericFact() {
        await Assert.That(Clr<bool>().IsNumber).IsFalse();
        await Assert.That(Clr<char>().IsNumber).IsFalse();
        await Assert.That(Clr<string>().IsNumber).IsFalse();
        await Assert.That(Clr<DayOfWeek>().IsNumber).IsFalse().Because("an enumeration is over a number; it is not one");
        await Assert.That(Clr<int?>().IsNumber).IsFalse().Because("a nullable form states its own facts; NullableOf names what it wraps");
        await Assert.That(Clr<DateTime>().IsNumber).IsFalse();
    }

    [Test]
    public async Task ClrTruthAndText_StateWhatTheyAre() {
        await Assert.That(Clr<bool>().IsBoolean).IsTrue();
        await Assert.That(Clr<int>().IsBoolean).IsFalse();
        await Assert.That((Clr<string>().IsText, Clr<string>().IsCharacter)).IsEqualTo((true, false));
        await Assert.That((Clr<char>().IsText, Clr<char>().IsCharacter)).IsEqualTo((true, true));
        await Assert.That(Clr<Guid>().IsText).IsFalse();
    }

    [Test]
    public async Task ClrTemporalTypes_StateTheirParts() {
        await Assert.That(Time(Clr<DateOnly>())).IsEqualTo((IsTemporal: true, HasDate: true, HasTime: false, HasOffset: false, IsDuration: false));
        await Assert.That(Time(Clr<TimeOnly>())).IsEqualTo((IsTemporal: true, HasDate: false, HasTime: true, HasOffset: false, IsDuration: false));
        await Assert.That(Time(Clr<DateTime>())).IsEqualTo((IsTemporal: true, HasDate: true, HasTime: true, HasOffset: false, IsDuration: false));
        await Assert.That(Time(Clr<DateTimeOffset>())).IsEqualTo((IsTemporal: true, HasDate: true, HasTime: true, HasOffset: true, IsDuration: false));
        await Assert.That(Time(Clr<TimeSpan>())).IsEqualTo((IsTemporal: true, HasDate: false, HasTime: false, HasOffset: false, IsDuration: true));
        await Assert.That(Clr<long>().IsTemporal).IsFalse();
    }

    [Test]
    public async Task ClrGuidBytesAndObject_StateWhatTheyAre() {
        await Assert.That(Clr<Guid>().IsUniqueIdentifier).IsTrue();
        await Assert.That(Clr<byte[]>().IsBinary).IsTrue();
        await Assert.That(Clr<int[]>().IsBinary).IsFalse();
        await Assert.That(Clr<object>().IsTopType).IsTrue();
        await Assert.That(Clr<string>().IsTopType).IsFalse();
    }

    [Test]
    public async Task ClrGroundTypes_AreAtomic_AndComposedTypesAreNot() {
        Type[] ground = [
            typeof(bool), typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort), typeof(uint),
            typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(char), typeof(DateTime),
            typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(DateTimeOffset), typeof(byte[]),
            typeof(object), typeof(nint), typeof(nuint),
        ];
        foreach (var type in ground)
            await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type).IsGround()).IsTrue().Because($"'{type}' is a ground type");

        Type[] composed = [typeof(Uri), typeof(int?), typeof(DayOfWeek), typeof(List<int>), typeof(KeyValuePair<int, int>), typeof(Action), typeof(int[])];
        foreach (var type in composed)
            await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type).IsGround()).IsFalse().Because($"'{type}' is not a ground type");
    }

    [Test]
    public async Task ShiftCountPolicy_AskedOfTheFact_AgreesWithTheClrTable_ForEveryClrGroundType() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = ClrBuiltInRules.IsPolicyShiftCount(definition);

            await Assert.That(byFact).IsEqualTo(ClrBuiltInRules.IsPolicyShiftCount(primitive))
                .Because($"'{primitive}' is a policy shift count by its representation exactly when the CLR table says so");
        }
    }

    [Test]
    public async Task IntegerFolding_AskedOfTheFact_IsExactlyTheFixedWidthIntegers() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = definition is { IsNumber: true, IsIntegral: true, IsNativeSized: false };

            await Assert.That(byFact).IsEqualTo(primitive.IsInteger())
                .Because($"'{primitive}': the fixed-width integers, never a native-sized one, a float, a decimal or a char");
        }
    }

    [Test]
    public async Task DomainGround_OfEveryClrGroundType_IsTheDomainsOwnRule() {
        (Type Type, Poly.DomainModeling.Meaning.DomainGround? Ground)[] table = [
            (typeof(string), Poly.DomainModeling.Meaning.DomainGround.Text),
            (typeof(int), Poly.DomainModeling.Meaning.DomainGround.Int32),
            (typeof(long), Poly.DomainModeling.Meaning.DomainGround.Int64),
            (typeof(float), Poly.DomainModeling.Meaning.DomainGround.Float32),
            (typeof(double), Poly.DomainModeling.Meaning.DomainGround.Float64),
            (typeof(decimal), Poly.DomainModeling.Meaning.DomainGround.Decimal),
            (typeof(bool), Poly.DomainModeling.Meaning.DomainGround.Boolean),
            (typeof(DateOnly), Poly.DomainModeling.Meaning.DomainGround.Date),
            (typeof(DateTime), Poly.DomainModeling.Meaning.DomainGround.DateTime),
            (typeof(TimeOnly), Poly.DomainModeling.Meaning.DomainGround.Time),
            (typeof(TimeSpan), Poly.DomainModeling.Meaning.DomainGround.Duration),
            (typeof(Guid), Poly.DomainModeling.Meaning.DomainGround.Guid),
            (typeof(byte[]), Poly.DomainModeling.Meaning.DomainGround.Binary),
            // Ground types the domain language has no rule for: they must not join a family because a fact exists.
            (typeof(char), null), (typeof(sbyte), null), (typeof(short), null), (typeof(byte), null), (typeof(ushort), null),
            (typeof(uint), null), (typeof(ulong), null), (typeof(nint), null), (typeof(nuint), null),
            (typeof(DateTimeOffset), null), (typeof(object), null),
            (typeof(Uri), null), (typeof(DayOfWeek), null), (typeof(int?), null),
        ];

        await Assert.That(table.Select(static row => row.Type).Where(ClrGround.IsGround).Count())
            .IsEqualTo(Enum.GetValues<ClrPrimitive>().Length).Because("every CLR ground type has a row");
        foreach (var (type, ground) in table)
            await Assert.That(Poly.DomainModeling.Meaning.DomainGrounds.Of(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)))
                .IsEqualTo(ground).Because($"'{type}'");
    }

    [Test]
    public async Task HostWithNoClrTypes_StatesANumberInItsOwnRepresentation() {
        var host = new LedgerHost();

        await Assert.That(Number(host.Amount)).IsEqualTo((IsNumber: true, IsIntegral: false, IsSigned: true, NumberBits: 96, IsDecimal: true, IsNativeSized: false))
            .Because("a host states a number no shared list of ids names");
        await Assert.That(host.Account.IsNumber).IsFalse();
    }
}