using Poly.DomainModeling.Lowering;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Test-only subject types and factory helpers for <see cref="PolicyEvaluator"/>
/// tests. These are not promoted into product code — subjects in production
/// should use real domain types or records with typed CLR properties.
///
/// Non-nullable property bags proven by WS8 spike. Use as the default subject
/// type for ad-hoc policy evaluation in tests.
/// </summary>
public static class PolicyTestSubjects {
    /// <summary>
    /// Creates a simple sample subject with the given age value for testing
    /// age-guard policies. The returned <see cref="SampleAgeSubject"/> has an
    /// <c>Age</c> property (non-nullable <c>int</c>, safe for VM).
    /// </summary>
    public static SampleAgeSubject SampleFromAge(int age) => new(age);

    /// <summary>
    /// Creates a sample <see cref="StrictBag"/> from named property values.
    /// Properties are non-nullable with safe defaults: missing values become
    /// 0, <c>string.Empty</c>, or 0m.
    /// </summary>
    public static StrictBag SampleFromBag(string? name, int? age, string? status, long? total) =>
        new(
            Age: age ?? 0,
            Name: name ?? string.Empty,
            Status: status ?? string.Empty,
            Total: total ?? 0
        );

    /// <summary>
    /// Non-nullable subject bag proven by WS8 spike. Use as the default
    /// subject type for ad-hoc policy evaluation in tests.
    /// </summary>
    public sealed record StrictBag(int Age, string Name, string Status, long Total);

    /// <summary>
    /// Minimal subject with a single <c>Age</c> property for age-guard policies.
    /// </summary>
    public sealed record SampleAgeSubject(int Age);
}