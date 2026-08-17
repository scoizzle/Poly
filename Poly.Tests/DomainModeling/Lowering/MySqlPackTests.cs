using Poly.DomainModeling;
using Poly.DomainModeling.Lowering;
using Poly.Packs.MySql;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// pack-2-4: MySql composes as an <see cref="IDomainLibrary"/> — adding the pack
/// applies the same type maps as <see cref="MySqlDefaults"/>.
/// </summary>
public class MySqlPackTests {
    [Test]
    public async Task AddPack_MySql_AppliesTypeMaps() {
        var builder = SessionBuilder.CreateEmpty();

        builder.Load(new MySqlLibrary());

        var inputs = builder.Build();

        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Text")).IsEqualTo("longtext");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Number")).IsEqualTo("bigint");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Int32")).IsEqualTo("int");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Boolean")).IsEqualTo("tinyint(1)");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("DateTime")).IsEqualTo("datetime(6)");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("DateOnly")).IsEqualTo("date");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Double")).IsEqualTo("double");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Decimal")).IsEqualTo("decimal(65,30)");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Guid")).IsEqualTo("char(36)");
        await Assert.That(inputs.TypeMaps.ToSqlColumnType("Binary")).IsEqualTo("blob");
    }
}