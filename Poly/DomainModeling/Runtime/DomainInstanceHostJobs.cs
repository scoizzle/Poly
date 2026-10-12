namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Host jobs the runtime entity type forwards to <see cref="DomainEntityInstance"/>.
/// The type definition built in <c>DomainEntityInstance.Runtime.cs</c> gives each job
/// an AST method whose body calls the method here with <c>this</c> as the instance.
/// Every job needs a <see cref="DomainEntityInstance"/> and throws on anything else.
/// </summary>
public static class DomainInstanceHostJobs {
    public static DomainResult EnsureUnique(object? instance, string propertyName, object? value) =>
        Receiver(instance, nameof(EnsureUnique)).EnsureUnique(propertyName, value);

    public static DomainResult EnsureUnique(object? instance, string propertyName, long value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static DomainResult EnsureUnique(object? instance, string propertyName, bool value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static DomainResult EnsureUnique(object? instance, string propertyName, string? value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static void Notify(object? instance, string stageName) =>
        Receiver(instance, nameof(Notify)).Notify(stageName);

    public static void Notify(object? instance, string stageName, string? previousStageName) =>
        Receiver(instance, nameof(Notify)).Notify(stageName, previousStageName);

    public static void LinkRelated(object? instance, string relationshipName, object? target) =>
        Receiver(instance, nameof(LinkRelated)).LinkRelated(relationshipName, target);

    public static DomainResult Create(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Receiver(instance, nameof(Create)).Create(name, values);

    public static DomainResult CreateIn(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Receiver(instance, nameof(CreateIn)).CreateIn(name, values);

    public static DomainResult ProbeCreate(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Receiver(instance, nameof(ProbeCreate)).ProbeCreate(name, values);

    /// <summary>
    /// Static <c>Type.Create(typeName, values)</c> from the compiled tree. The
    /// creating instance is the ambient interpreter subject; null values are
    /// dropped so the store applies the same defaults as the omitted-argument path.
    /// </summary>
    public static DomainResult TypedCreate(string typeName, Dictionary<string, object?> values) =>
        DomainEntityInstance.Ambient is { } host
            ? host.Create(typeName, TrimNulls(values))
            : DomainResult.Failure($"Create '{typeName}' has no ambient instance.");

    static Dictionary<string, object?> TrimNulls(Dictionary<string, object?> values) {
        var trimmed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in values) {
            if (kv.Value is not null)
                trimmed[kv.Key] = kv.Value;
        }
        return trimmed;
    }

    /// <summary>An empty values bag. The name/value-pair create jobs start from it.</summary>
    public static Dictionary<string, object?> Values() => new(StringComparer.Ordinal);

    /// <summary>Sets one value in <paramref name="values"/> and returns the same bag.</summary>
    public static Dictionary<string, object?> With(Dictionary<string, object?> values, string name, object? value) {
        values[name] = value;
        return values;
    }

    public static Dictionary<string, object?> With(Dictionary<string, object?> values, string name, long value) =>
        With(values, name, (object?)value);

    public static Dictionary<string, object?> With(Dictionary<string, object?> values, string name, bool value) =>
        With(values, name, (object?)value);

    public static Dictionary<string, object?> With(Dictionary<string, object?> values, string name, string? value) =>
        With(values, name, (object?)value);

    private static DomainEntityInstance Receiver(object? instance, string job) =>
        instance as DomainEntityInstance
            ?? throw new InvalidOperationException(
                $"{job} requires a domain instance, got {instance?.GetType().Name ?? "null"}.");
}