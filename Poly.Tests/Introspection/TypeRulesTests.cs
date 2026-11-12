using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-03b: what an <see cref="ITypeDefinition"/> answers about conversions and operators.
/// </summary>
public class TypeRulesTests {
    /// <summary>
    /// B7: the public conversion door as it stood on master before T-03b, over every CLR primitive pair.
    /// One row per destination, one cell per source in <see cref="Prim"/> order. A cell is three letters:
    /// GetConversionFrom (N none, I implicit, E explicit), IsAssignableFrom (T/F), IsNumericWideningFrom (T/F).
    /// Captured from origin/master 51b9453e; never regenerate it from the code under test.
    /// </summary>
    private static readonly Dictionary<Prim, string> MasterDoorMatrix = new() {
        [Poly.Introspection.PrimitiveType.Boolean] = "NTF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int8] = "NFF NTT NFF NFF NFF NFT NFF NFF NFF NFF NFF EFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int16] = "NFF NFT NTT NFF NFF NFT NFT NFF NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int32] = "NFF NFT NFT NTT NFF NFT NFT NFT NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int64] = "NFF NFT NFT NFT NTT NFT NFT NFT NFT NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt8] = "NFF NFT NFF NFF NFF NTT NFF NFF NFF NFF NFF EFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt16] = "NFF NFT NFT NFF NFF NFT NTT NFF NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt32] = "NFF NFT NFT NFT NFF NFT NFT NTT NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt64] = "NFF NFT NFT NFT NFT NFT NFT NFT NTT NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Float32] = "NFF NFT NFT NFT NFT NFT NFT NFT NFT NTT NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Float64] = "NFF NFT NFT NFT NFT NFT NFT NFT NFT NFT NTT EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Decimal] = "NFF ITT ITT ITT ITT ITT ITT ITT ITT EFT EFT NTT NFF ITT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.String] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Char] = "NFF NFT NFT NFF NFF NFT NFT NFF NFF NFF NFF EFF NFF NTT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.DateTime] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.DateOnly] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.TimeOnly] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.TimeSpan] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Guid] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF",
        [Poly.Introspection.PrimitiveType.ByteArray] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF",
        [Poly.Introspection.PrimitiveType.Structure] = "NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF",
    };

    [Test]
    public async Task PublicConversionDoor_IsUnchanged() {
        var prims = Enum.GetValues<Prim>();
        var mismatches = new List<string>();
        foreach (var to in prims) {
            var cells = MasterDoorMatrix[to].Split(' ');
            var target = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(to.GetClrType()!);
            for (var i = 0; i < prims.Length; i++) {
                var source = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(prims[i].GetClrType()!);
                var conversion = target.GetConversionFrom(source);
                var actual = string.Concat(
                    conversion is null ? 'N' : conversion.Value.Kind == ConversionOperatorKind.Implicit ? 'I' : 'E',
                    target.IsAssignableFrom(source) ? 'T' : 'F',
                    target.IsNumericWideningFrom(source) ? 'T' : 'F');
                if (actual != cells[i])
                    mismatches.Add($"{prims[i]} -> {to}: expected {cells[i]}, was {actual}");
            }
        }
        await Assert.That(mismatches).IsEmpty();
    }
}