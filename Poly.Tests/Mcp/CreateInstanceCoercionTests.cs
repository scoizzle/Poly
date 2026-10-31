using Poly.Mcp.Tools;

namespace Poly.Tests.Mcp;

/// <summary>
/// F32: create_instance coerces JSON property values to the entity's declared
/// property types. ISO strings parse to Date/DateTime/Time/Duration; wrong-typed
/// raw values fail the tool call instead of reaching the store and breaking
/// typed policy comparisons later ("String vs DateTime").
/// </summary>
public class CreateInstanceCoercionTests {
    private const string CoercionDomain = """
        domain CoerceProbe
        uses temporal

        Job: entity {
          Name: Text
          ExpiresAt: DateTime
          Due: Date optional
          Window: Duration optional
          OpensAt: Time optional

          IsExpired: policy { ExpiresAt < Now }
        }
        """;

    private static string Property(DomainToolResponse response, string name) {
        var json = System.Text.Json.JsonSerializer.Serialize(response.Data);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.GetProperty("instance").GetProperty("properties").EnumerateArray()) {
            if (property.GetProperty("name").GetString() == name)
                return property.GetProperty("value").GetString()
                    ?? throw new InvalidOperationException($"null {name}");
        }
        throw new InvalidOperationException($"missing property {name}");
    }

    private static string InstanceId(DomainToolResponse response) {
        var json = System.Text.Json.JsonSerializer.Serialize(response.Data);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("instance").GetProperty("instanceId").GetString()
            ?? throw new InvalidOperationException("missing instanceId");
    }

    private static string CreateAndId(string sessionId, string entity, string props) {
        var create = McpTestTools.Runtime.CreateInstance(sessionId, entity, props);
        if (!create.Success)
            throw new InvalidOperationException($"create_instance {entity}: {create.Message}");
        var json = System.Text.Json.JsonSerializer.Serialize(create.Data);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("instance", out var instance)
            && instance.TryGetProperty("instanceId", out var id))
            return id.GetString() ?? throw new InvalidOperationException("missing instanceId");
        throw new InvalidOperationException("missing instance payload");
    }

    [Test]
    public async Task IsoStrings_CoerceToDeclaredTemporalTypes_PolicyComparesAsTyped() {
        var (sessionId, _) = McpTestTools.Store.Create("CoerceTemporal");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, CoercionDomain).Success).IsTrue();

        var create = McpTestTools.Runtime.CreateInstance(sessionId, "Job",
            """{"Name":"J","ExpiresAt":"2026-01-01T00:00:00Z","Due":"2026-01-01","Window":"00:30:00","OpensAt":"09:00:00"}""");
        await Assert.That(create.Success).IsTrue();
        var jobId = CreateAndId(sessionId, "Job", """{"Name":"J2","ExpiresAt":"2026-01-01T00:00:00Z"}""");
        _ = jobId;

        // The F32 repro: an un-coerced string ExpiresAt made IsExpired throw
        // "Cannot compare values of different runtime types: 'String' vs 'DateTime'".
        var policy = McpTestTools.Policy.EvaluatePolicy(sessionId, "Job", "IsExpired", jobId);
        await Assert.That(policy.Success).IsTrue();
        await Assert.That(policy.Message).Contains("true");
    }

    [Test]
    public async Task DateTimeOffset_AndZulu_StoreTheSameUtcInstant() {
        var (sessionId, _) = McpTestTools.Store.Create("CoerceOffset");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, CoercionDomain).Success).IsTrue();

        var zulu = McpTestTools.Runtime.CreateInstance(sessionId, "Job",
            """{"Name":"Z","ExpiresAt":"2026-01-01T00:00:00Z"}""");
        var offset = McpTestTools.Runtime.CreateInstance(sessionId, "Job",
            """{"Name":"O","ExpiresAt":"2026-01-01T00:00:00+00:00"}""");
        await Assert.That(zulu.Success).IsTrue();
        await Assert.That(offset.Success).IsTrue();
        await Assert.That(Property(offset, "ExpiresAt")).IsEqualTo(Property(zulu, "ExpiresAt"));

        var zuluPolicy = McpTestTools.Policy.EvaluatePolicy(
            sessionId, "Job", "IsExpired", InstanceId(zulu));
        var offsetPolicy = McpTestTools.Policy.EvaluatePolicy(
            sessionId, "Job", "IsExpired", InstanceId(offset));
        await Assert.That(zuluPolicy.Success).IsTrue();
        await Assert.That(offsetPolicy.Success).IsTrue();
        await Assert.That(offsetPolicy.Message).IsEqualTo(zuluPolicy.Message);
    }

    [Test]
    public async Task WrongTypedValue_FailsToolCall_NamingPropertyAndExpectedType() {
        var (sessionId, _) = McpTestTools.Store.Create("CoerceWrong");
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, CoercionDomain).Success).IsTrue();

        var numberForDateTime = McpTestTools.Runtime.CreateInstance(sessionId, "Job",
            """{"Name":"J","ExpiresAt":12345}""");
        await Assert.That(numberForDateTime.Success).IsFalse();
        await Assert.That(numberForDateTime.Message).Contains("ExpiresAt");
        await Assert.That(numberForDateTime.Message).Contains("DateTime");

        var badDate = McpTestTools.Runtime.CreateInstance(sessionId, "Job",
            """{"Name":"J","Due":"not-a-date"}""");
        await Assert.That(badDate.Success).IsFalse();
        await Assert.That(badDate.Message).Contains("Due");
        await Assert.That(badDate.Message).Contains("Date");
    }

    [Test]
    public async Task WrongTypedNumberAndBoolean_FailLoud() {
        var (sessionId, _) = McpTestTools.Store.Create("CoerceScalar");
        var dsl = """
            domain ScalarProbe
            uses temporal

            Thing: entity {
              Count: Number
              On: Boolean
            }
            """;
        await Assert.That(McpTestTools.Dsl.ApplyDsl(sessionId, dsl).Success).IsTrue();

        var stringNumber = McpTestTools.Runtime.CreateInstance(sessionId, "Thing", """{"Count":"5"}""");
        await Assert.That(stringNumber.Success).IsFalse();
        await Assert.That(stringNumber.Message).Contains("Count");
        await Assert.That(stringNumber.Message).Contains("Number");

        var textBoolean = McpTestTools.Runtime.CreateInstance(sessionId, "Thing", """{"On":"yes"}""");
        await Assert.That(textBoolean.Success).IsFalse();
        await Assert.That(textBoolean.Message).Contains("On");
        await Assert.That(textBoolean.Message).Contains("Boolean");
    }
}