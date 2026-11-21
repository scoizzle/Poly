using System.Reflection;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #563: a column default must not swallow a value. EF treats a value equal to a property's sentinel as "unset" when
/// the property has a store default, so this pins the measured behaviour on the generated model: a value that equals
/// the CLR default (false) is still written, not replaced by the store default the column also declares.
/// </summary>
public class DbContextDefaultValueTests {
    private const string Fixture = """
        domain Freight
        uses sqlite
        uses http

        Carrier: entity {
          Name: Text unique length(2, 80)
          Active: Boolean default(true)
        }
        """;

    /// <summary>A value equal to the CLR default is a value: <c>Create(…, active: false)</c> is written to the
    /// column, not replaced by the store default it also declares.</summary>
    [Test]
    public async Task Create_ValueEqualToTheClrDefault_IsWritten_NotReplacedByTheStoreDefault() {
        using var model = EfModel.Create(Fixture, "Freight").EnsureSchema();

        var carrier = model.Assembly.GetTypes().Single(t => t.Name == "Carrier");
        var create = carrier.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var result = create.Invoke(null, [model.Db, "AB", false])!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();

        model.Db.Add(result.GetType().GetProperty("Value")!.GetValue(result)!);
        await model.Db.SaveChangesAsync();

        await Assert.That(Scalar(model, "SELECT active FROM Carriers WHERE name = 'AB'")).IsEqualTo("0");
    }

    private static string Scalar(EfModel model, string sql) {
        using var command = model.Connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }
}