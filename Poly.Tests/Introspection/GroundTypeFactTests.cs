using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a ground type is, is a fact its platform states (#998 S4/S5): numeric representation, text, truth, time. No
/// shared list of primitive ids stands between a platform and a consumer.
/// </summary>
public class GroundTypeFactTests {
    private static ITypeDefinition Clr<T>() => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(T));

    [Test]
    public async Task ClrNumbers_StateTheirRepresentation() {
        await Assert.That(Clr<sbyte>().GetFact<Numeric>()).IsEqualTo(new Numeric(IsIntegral: true, IsSigned: true, Bits: 8));
        await Assert.That(Clr<short>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, true, 16));
        await Assert.That(Clr<int>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, true, 32));
        await Assert.That(Clr<long>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, true, 64));
        await Assert.That(Clr<byte>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, false, 8));
        await Assert.That(Clr<ushort>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, false, 16));
        await Assert.That(Clr<uint>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, false, 32));
        await Assert.That(Clr<ulong>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, false, 64));
        await Assert.That(Clr<float>().GetFact<Numeric>()).IsEqualTo(new Numeric(false, true, 32));
        await Assert.That(Clr<double>().GetFact<Numeric>()).IsEqualTo(new Numeric(false, true, 64));
        await Assert.That(Clr<decimal>().GetFact<Numeric>()).IsEqualTo(new Numeric(false, true, 128, IsDecimal: true));
        await Assert.That(Clr<nint>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, true, IntPtr.Size * 8, IsNativeSized: true));
        await Assert.That(Clr<nuint>().GetFact<Numeric>()).IsEqualTo(new Numeric(true, false, IntPtr.Size * 8, IsNativeSized: true));
    }

    [Test]
    public async Task ClrNonNumbers_StateNoNumericFact() {
        await Assert.That(Clr<bool>().GetFact<Numeric>()).IsNull();
        await Assert.That(Clr<char>().GetFact<Numeric>()).IsNull();
        await Assert.That(Clr<string>().GetFact<Numeric>()).IsNull();
        await Assert.That(Clr<DayOfWeek>().GetFact<Numeric>()).IsNull().Because("an enumeration is over a number; it is not one");
        await Assert.That(Clr<int?>().GetFact<Numeric>()).IsNull().Because("a nullable form states its own facts; NullableOf names what it wraps");
        await Assert.That(Clr<DateTime>().GetFact<Numeric>()).IsNull();
    }

    [Test]
    public async Task ClrTruthAndText_StateWhatTheyAre() {
        await Assert.That(Clr<bool>().GetFact<Truth>()).IsNotNull();
        await Assert.That(Clr<int>().GetFact<Truth>()).IsNull();
        await Assert.That(Clr<string>().GetFact<Text>()).IsEqualTo(new Text(IsSingleCharacter: false));
        await Assert.That(Clr<char>().GetFact<Text>()).IsEqualTo(new Text(IsSingleCharacter: true));
        await Assert.That(Clr<Guid>().GetFact<Text>()).IsNull();
    }

    [Test]
    public async Task ClrTemporalTypes_StateTheirParts() {
        await Assert.That(Clr<DateOnly>().GetFact<Temporal>()).IsEqualTo(new Temporal(HasDate: true, HasTime: false));
        await Assert.That(Clr<TimeOnly>().GetFact<Temporal>()).IsEqualTo(new Temporal(HasDate: false, HasTime: true));
        await Assert.That(Clr<DateTime>().GetFact<Temporal>()).IsEqualTo(new Temporal(HasDate: true, HasTime: true));
        await Assert.That(Clr<DateTimeOffset>().GetFact<Temporal>()).IsEqualTo(new Temporal(true, true, HasOffset: true));
        await Assert.That(Clr<TimeSpan>().GetFact<Temporal>()).IsEqualTo(new Temporal(false, false, IsDuration: true));
        await Assert.That(Clr<long>().GetFact<Temporal>()).IsNull();
    }

    [Test]
    public async Task ClrGuidBytesAndObject_StateWhatTheyAre() {
        await Assert.That(Clr<Guid>().GetFact<UniqueIdentifier>()).IsNotNull();
        await Assert.That(Clr<byte[]>().GetFact<Binary>()).IsNotNull();
        await Assert.That(Clr<int[]>().GetFact<Binary>()).IsNull();
        await Assert.That(Clr<object>().GetFact<TopType>()).IsNotNull();
        await Assert.That(Clr<string>().GetFact<TopType>()).IsNull();
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
            await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type).GetFact<Atomic>()).IsNotNull().Because($"'{type}' is a ground type");

        Type[] composed = [typeof(Uri), typeof(int?), typeof(DayOfWeek), typeof(List<int>), typeof(KeyValuePair<int, int>), typeof(Action), typeof(int[])];
        foreach (var type in composed)
            await Assert.That(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type).GetFact<Atomic>()).IsNull().Because($"'{type}' is not a ground type");
    }

    [Test]
    public async Task ShiftCountPolicy_AskedOfTheFact_AgreesWithTheClrTable_ForEveryClrGroundType() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = definition.GetFact<Numeric>() is { } number && ClrBuiltInRules.IsPolicyShiftCount(number);

            await Assert.That(byFact).IsEqualTo(ClrBuiltInRules.IsPolicyShiftCount(primitive))
                .Because($"'{primitive}' is a policy shift count by its representation exactly when the CLR table says so");
        }
    }

    [Test]
    public async Task IntegerFolding_AskedOfTheFact_IsExactlyTheFixedWidthIntegers() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = definition.GetFact<Numeric>() is { IsIntegral: true, IsNativeSized: false };

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

        await Assert.That(table.Select(static row => row.Type).Where(ClrGroundTypes.IsGround).Count())
            .IsEqualTo(Enum.GetValues<ClrPrimitive>().Length).Because("every CLR ground type has a row");
        foreach (var (type, ground) in table)
            await Assert.That(Poly.DomainModeling.Meaning.DomainGrounds.Of(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)))
                .IsEqualTo(ground).Because($"'{type}'");
    }

    [Test]
    public async Task HostWithNoClrTypes_StatesANumberInItsOwnRepresentation() {
        var host = new LedgerHost();

        await Assert.That(host.Amount.GetFact<Numeric>()).IsEqualTo(new Numeric(IsIntegral: false, IsSigned: true, Bits: 96, IsDecimal: true))
            .Because("a host states a number no shared list of ids names");
        await Assert.That(host.Account.GetFact<Numeric>()).IsNull();
    }
}