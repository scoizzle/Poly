using System.Globalization;

using Poly.DomainModeling;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>One generated step: a create, an action invocation, or a policy evaluation.</summary>
public sealed record ScenarioStep(string Kind, string Name, IReadOnlyList<(string Name, object? Value)> Args) {
    public override string ToString() =>
        Args.Count == 0 ? $"{Kind} {Name}" : $"{Kind} {Name}({string.Join(", ", Args.Select(a => $"{a.Name}={Show(a.Value)}"))})";

    static string Show(object? value) => value switch {
        null => "null",
        string s => "\"" + s + "\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}

/// <summary>One generated script: an entity, and the ordered steps run against it.</summary>
public sealed record ScenarioScript(string Label, string Entity, IReadOnlyList<ScenarioStep> Steps) {
    public string Text => $"{Label} [{Entity}] " + string.Join("; ", Steps);
}

/// <summary>
/// #374 S2: a seeded generator of step scripts over the stage graph, run through
/// <see cref="ParityScenario"/>. It reads the domain's <b>facts</b> (entities, properties, the actions
/// each stage declares, policies) and emits random legal <i>and illegal</i> sequences; it never
/// predicts a result. Whether an action is legal from the current stage is decided by the lowered
/// module on each side, and the differential compares what the two sides did.
/// <para>
/// <b>Determinism.</b> A pure function of <c>(domain facts, seed, scripts, maxSteps)</c>: a local
/// SplitMix64, no clock, no GUID, no ambient <see cref="Random"/>. The seed is in every label.
/// </para>
/// <para>
/// <b>Bound.</b> <see cref="MaxScripts"/> scripts of at most <see cref="MaxSteps"/> steps each; larger
/// requests are refused. Sampling, never enumeration.
/// </para>
/// </summary>
public static class ScenarioGenerator {
    public const int MaxScripts = 200;
    public const int MaxSteps = 12;
    public const int DefaultScripts = 40;
    public const int DefaultSteps = 8;
    public const ulong DefaultSeed = 374;
    const int CreateAttempts = 3;

    /// <summary>What the generator cannot supply, so the thing is left out rather than guessed.</summary>
    public static IReadOnlyList<(string Shape, string Why)> NotGenerated { get; } = [
        ("actions or properties typed as another entity or a value type",
            "an argument needs an earlier-created linked instance or a composed value; ParitySide links through Create results, which a flat script does not model"),
        ("link, unique-key and storage steps",
            "uniqueness needs a recording storage port and distinct-key bookkeeping; that is a separate scenario family, not a flat step script"),
        ("expected outcomes",
            "the generator never states what should happen: the lowered module's answer on each side is the data, and only agreement between them is asserted"),
    ];

    public static IReadOnlyList<ScenarioScript> Generate(
        Domain domain, ulong seed, int scripts = DefaultScripts, int maxSteps = DefaultSteps) {
        ArgumentNullException.ThrowIfNull(domain);
        if (scripts is < 0 or > MaxScripts)
            throw new ArgumentOutOfRangeException(nameof(scripts), scripts, $"scripts is bounded to 0..{MaxScripts}");
        if (maxSteps is < 1 or > MaxSteps)
            throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, $"steps is bounded to 1..{MaxSteps}");

        var entities = domain.Types.OfType<Entity>().Where(e => ActionsOf(e).Count > 0).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        if (entities.Count == 0)
            return [];
        var enums = domain.Types.OfType<EnumType>().ToDictionary(e => e.Name, StringComparer.Ordinal);

        var result = new List<ScenarioScript>(scripts);
        for (var index = 0; index < scripts; index++) {
            var rng = new SplitMix(seed ^ ((ulong)(index + 1) * 0x9E3779B97F4A7C15UL));
            var entity = entities[rng.Next(entities.Count)];
            var steps = new List<ScenarioStep>();
            for (var attempt = 0; attempt < CreateAttempts; attempt++)
                steps.Add(new("create", entity.Name, CreateArgs(rng, entity, enums)));
            var actions = ActionsOf(entity);
            var count = 1 + rng.Next(maxSteps);
            for (var i = 0; i < count; i++) {
                if (entity.Policies.Count > 0 && rng.Next(6) == 0) {
                    steps.Add(new("policy", entity.Policies[rng.Next(entity.Policies.Count)].Name, []));
                    continue;
                }
                var action = actions[rng.Next(actions.Count)];
                var args = ArgsFor(rng, action.Parameters, enums);
                if (args is null)
                    continue;
                steps.Add(new("invoke", action.Name, args));
            }
            result.Add(new($"seed={seed}#{index}", entity.Name, steps));
        }
        return result;
    }

    /// <summary>The whole set as one text: what the determinism check diffs.</summary>
    public static string Render(IReadOnlyList<ScenarioScript> scripts) => string.Join("\n", scripts.Select(s => s.Text));

    /// <summary>
    /// Runs a script against one side. Creates are attempted until one succeeds; later steps need a
    /// current entity, so a script whose creates all fail records only the creates, identically on both sides.
    /// </summary>
    public static void Apply(ParitySide side, ScenarioScript script) {
        var created = false;
        foreach (var step in script.Steps) {
            switch (step.Kind) {
                case "create":
                    if (!created && side.Create(step.Name, [.. step.Args]) is not null)
                        created = true;
                    break;
                case "invoke" when created:
                    side.Invoke(step.Name, [.. step.Args]);
                    break;
                case "policy" when created:
                    side.EvaluatePolicy(step.Name);
                    break;
            }
        }
    }

    static List<Poly.DomainModeling.Ontology.Action> ActionsOf(Entity entity) =>
        [.. entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)).DistinctBy(a => a.Name)];

    static (string Name, object? Value)[] CreateArgs(SplitMix rng, Entity entity, Dictionary<string, EnumType> enums) {
        var args = new List<(string, object?)>();
        foreach (var property in entity.Properties) {
            if (rng.Next(3) == 0)
                continue;
            if (ValueFor(rng, property.Type.TypeName, enums) is { } value)
                args.Add((property.Name, value.Value));
        }
        return [.. args];
    }

    static (string Name, object? Value)[]? ArgsFor(SplitMix rng, IReadOnlyList<Property> parameters, Dictionary<string, EnumType> enums) {
        var args = new List<(string, object?)>();
        foreach (var parameter in parameters) {
            if (ValueFor(rng, parameter.Type.TypeName, enums) is not { } value)
                return null;
            args.Add((parameter.Name, value.Value));
        }
        return [.. args];
    }

    static readonly string[] Texts = ["", " ", "a", "Ada", "AB", "a1", "hello world", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"];
    static readonly long[] Numbers = [0L, 1L, -1L, 3L, 5L, 10L, 11L, 100L, long.MaxValue];

    // A one-element wrapper so that "no value for this type" is distinct from a null value.
    static (object? Value, bool Has)? ValueFor(SplitMix rng, string typeName, Dictionary<string, EnumType> enums) {
        if (typeName == "Text") return (Texts[rng.Next(Texts.Length)], true);
        if (typeName == "Number") return (Numbers[rng.Next(Numbers.Length)], true);
        if (typeName == "Boolean") return (rng.Next(2) == 0, true);
        if (enums.TryGetValue(typeName, out var enumType) && enumType.MemberNames.Count > 0)
            return (enumType.MemberNames[rng.Next(enumType.MemberNames.Count)], true);
        return null;
    }

    sealed class SplitMix(ulong state) {
        ulong _state = state;

        ulong NextULong() {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int exclusiveMax) => (int)(NextULong() % (ulong)exclusiveMax);
    }
}