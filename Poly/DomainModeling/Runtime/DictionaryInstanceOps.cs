namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Store jobs named by the operation AST. Dictionary <c>this</c> calls these
/// static methods; it does not Member-read the directory. The directory still
/// identifies instances as <see cref="DomainEntityInstance"/>.
/// </summary>
public static class DictionaryInstanceOps {
    public static DomainResult EnsureUnique(object? instance, string propertyName, object? value) {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (instance is DomainEntityInstance dei)
            return dei.EnsureUnique(propertyName, value);
        return DomainResult.Success();
    }

    public static DomainResult EnsureUnique(object? instance, string propertyName, long value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static DomainResult EnsureUnique(object? instance, string propertyName, bool value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static DomainResult EnsureUnique(object? instance, string propertyName, string? value) =>
        EnsureUnique(instance, propertyName, (object?)value);

    public static void Notify(object? instance, string stageName) {
        if (instance is DomainEntityInstance dei)
            dei.Notify(stageName);
    }

    public static void Notify(object? instance, string stageName, string? previousStageName) {
        if (instance is DomainEntityInstance dei)
            dei.Notify(stageName, previousStageName);
    }

    public static void LinkRelated(object? instance, string relationshipName, object? target) {
        if (instance is not DomainEntityInstance dei)
            throw new InvalidOperationException(
                $"LinkRelated requires a domain instance, got {instance?.GetType().Name ?? "null"}.");
        dei.LinkRelated(relationshipName, target);
    }

    public static DomainResult Create(object? instance, string name) =>
        Job(instance, "Create", name, Empty());

    public static DomainResult Create(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Job(instance, "Create", name, values);

    public static DomainResult CreateIn(object? instance, string name) =>
        Job(instance, "CreateIn", name, Empty());

    public static DomainResult CreateIn(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Job(instance, "CreateIn", name, values);

    public static DomainResult ProbeCreate(object? instance, string name) =>
        Job(instance, "ProbeCreate", name, Empty());

    public static DomainResult ProbeCreate(object? instance, string name, IReadOnlyDictionary<string, object?> values) =>
        Job(instance, "ProbeCreate", name, values);

    public static DomainResult Create(object? instance, string name, string p0, long v0) =>
        Pairs(instance, "Create", name, p0, v0);

    public static DomainResult Create(object? instance, string name, string p0, string? v0) =>
        Pairs(instance, "Create", name, p0, v0);

    public static DomainResult Create(object? instance, string name, string p0, bool v0) =>
        Pairs(instance, "Create", name, p0, v0);

    public static DomainResult Create(object? instance, string name, string p0, object? v0) =>
        Pairs(instance, "Create", name, p0, v0);

    public static DomainResult CreateIn(object? instance, string name, string p0, long v0) =>
        Pairs(instance, "CreateIn", name, p0, v0);

    public static DomainResult CreateIn(object? instance, string name, string p0, string? v0) =>
        Pairs(instance, "CreateIn", name, p0, v0);

    public static DomainResult CreateIn(object? instance, string name, string p0, bool v0) =>
        Pairs(instance, "CreateIn", name, p0, v0);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0) =>
        Pairs(instance, "CreateIn", name, p0, v0);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, long v0) =>
        Pairs(instance, "ProbeCreate", name, p0, v0);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, string? v0) =>
        Pairs(instance, "ProbeCreate", name, p0, v0);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, bool v0) =>
        Pairs(instance, "ProbeCreate", name, p0, v0);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0) =>
        Pairs(instance, "ProbeCreate", name, p0, v0);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14);

    public static DomainResult Create(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14, string p15, object? v15) =>
        Pairs(instance, "Create", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14, p15, v15);

    public static DomainResult CreateIn(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14, string p15, object? v15) =>
        Pairs(instance, "CreateIn", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14, p15, v15);

    public static DomainResult ProbeCreate(object? instance, string name, string p0, object? v0, string p1, object? v1, string p2, object? v2, string p3, object? v3, string p4, object? v4, string p5, object? v5, string p6, object? v6, string p7, object? v7, string p8, object? v8, string p9, object? v9, string p10, object? v10, string p11, object? v11, string p12, object? v12, string p13, object? v13, string p14, object? v14, string p15, object? v15) =>
        Pairs(instance, "ProbeCreate", name, p0, v0, p1, v1, p2, v2, p3, v3, p4, v4, p5, v5, p6, v6, p7, v7, p8, v8, p9, v9, p10, v10, p11, v11, p12, v12, p13, v13, p14, v14, p15, v15);

    private static Dictionary<string, object?> Empty() =>
        new(StringComparer.Ordinal);

    private static DomainResult Job(
        object? instance,
        string job,
        string name,
        IReadOnlyDictionary<string, object?> values) {
        if (instance is not DomainEntityInstance dei)
            throw new InvalidOperationException(
                $"{job} requires a domain instance, got {instance?.GetType().Name ?? "null"}.");
        return job switch {
            "Create" => dei.Create(name, values),
            "CreateIn" => dei.CreateIn(name, values),
            "ProbeCreate" => dei.ProbeCreate(name, values),
            _ => DomainResult.Failure($"Unknown create job '{job}'.")
        };
    }

    private static DomainResult Pairs(object? instance, string job, string name, params object?[] flat) {
        if (flat.Length % 2 != 0)
            return DomainResult.Failure($"{job} arguments must be name/value pairs.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < flat.Length; i += 2) {
            if (flat[i] is not string key || key.Length == 0)
                return DomainResult.Failure($"{job} property names must be strings.");
            values[key] = flat[i + 1];
        }
        return Job(instance, job, name, values);
    }
}