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
