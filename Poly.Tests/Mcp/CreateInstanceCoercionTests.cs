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

    private static string CreateAndId(string sessionId, string entity, string props) {
        var create = McpTestTools.Surface.Create(sessionId, entity, props);
        if (!create.Ok)
            throw new InvalidOperationException($"create_instance {entity}: {create.Summary}");
        return create.InstanceId;
    }

    [Test]
    public async Task IsoStrings_CoerceToDeclaredTemporalTypes_PolicyComparesAsTyped() {
        var sessionId = McpTestTools.Surface.Open("CoerceTemporal");
        await Assert.That(McpTestTools.Surface.Define(sessionId, CoercionDomain).Success).IsTrue();

        var create = McpTestTools.Surface.Create(sessionId, "Job",
            """{"Name":"J","ExpiresAt":"2026-01-01T00:00:00Z","Due":"2026-01-01","Window":"00:30:00","OpensAt":"09:00:00"}""");
        await Assert.That(create.Ok).IsTrue();
        var jobId = CreateAndId(sessionId, "Job", """{"Name":"J2","ExpiresAt":"2026-01-01T00:00:00Z"}""");
        _ = jobId;

        // The F32 repro: an un-coerced string ExpiresAt made IsExpired throw
        // "Cannot compare values of different runtime types: 'String' vs 'DateTime'".
        var policy = McpTestTools.Surface.Evaluate(sessionId, "Job", "IsExpired", jobId);
        await Assert.That(policy.Ok).IsTrue();
        await Assert.That(policy.Summary).Contains("true");
    }

    [Test]
    public async Task DateTimeOffset_AndZulu_StoreTheSameUtcInstant() {
        var sessionId = McpTestTools.Surface.Open("CoerceOffset");
        await Assert.That(McpTestTools.Surface.Define(sessionId, CoercionDomain).Success).IsTrue();

        var zulu = McpTestTools.Surface.Create(sessionId, "Job",
            """{"Name":"Z","ExpiresAt":"2026-01-01T00:00:00Z"}""");
        var offset = McpTestTools.Surface.Create(sessionId, "Job",
            """{"Name":"O","ExpiresAt":"2026-01-01T00:00:00+00:00"}""");
        await Assert.That(zulu.Ok).IsTrue();
        await Assert.That(offset.Ok).IsTrue();
        await Assert.That(offset.Property("ExpiresAt")).IsEqualTo(zulu.Property("ExpiresAt"));

        var zuluPolicy = McpTestTools.Surface.Evaluate(
            sessionId, "Job", "IsExpired", zulu.InstanceId);
        var offsetPolicy = McpTestTools.Surface.Evaluate(
            sessionId, "Job", "IsExpired", offset.InstanceId);
        await Assert.That(zuluPolicy.Ok).IsTrue();
        await Assert.That(offsetPolicy.Ok).IsTrue();
        await Assert.That(offsetPolicy.Summary).IsEqualTo(zuluPolicy.Summary);
    }

    [Test]
    public async Task WrongTypedValue_FailsToolCall_NamingPropertyAndExpectedType() {
        var sessionId = McpTestTools.Surface.Open("CoerceWrong");
        await Assert.That(McpTestTools.Surface.Define(sessionId, CoercionDomain).Success).IsTrue();

        var numberForDateTime = McpTestTools.Surface.Create(sessionId, "Job",
            """{"Name":"J","ExpiresAt":12345}""");
        await Assert.That(numberForDateTime.Ok).IsFalse();
        await Assert.That(numberForDateTime.Summary).Contains("ExpiresAt");
        await Assert.That(numberForDateTime.Summary).Contains("DateTime");

        var badDate = McpTestTools.Surface.Create(sessionId, "Job",
            """{"Name":"J","Due":"not-a-date"}""");
        await Assert.That(badDate.Ok).IsFalse();
        await Assert.That(badDate.Summary).Contains("Due");
        await Assert.That(badDate.Summary).Contains("Date");
    }

    [Test]
    public async Task WrongTypedNumberAndBoolean_FailLoud() {
        var sessionId = McpTestTools.Surface.Open("CoerceScalar");
        var dsl = """
            domain ScalarProbe
            uses temporal

            Thing: entity {
              Count: Number
              On: Boolean
            }
            """;
        await Assert.That(McpTestTools.Surface.Define(sessionId, dsl).Success).IsTrue();

        var stringNumber = McpTestTools.Surface.Create(sessionId, "Thing", """{"Count":"5"}""");
        await Assert.That(stringNumber.Ok).IsFalse();
        await Assert.That(stringNumber.Summary).Contains("Count");
        await Assert.That(stringNumber.Summary).Contains("Number");

        var textBoolean = McpTestTools.Surface.Create(sessionId, "Thing", """{"On":"yes"}""");
        await Assert.That(textBoolean.Ok).IsFalse();
        await Assert.That(textBoolean.Summary).Contains("On");
        await Assert.That(textBoolean.Summary).Contains("Boolean");
    }
}