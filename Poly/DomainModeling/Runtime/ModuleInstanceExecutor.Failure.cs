using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Runtime;

// Failure partial: classifies an action failure message into the outcome failure and its policy guard.
public sealed partial class ModuleInstanceExecutor {
    private ModuleOutcome MapFailure(Action action, string? message) {
        message ??= $"invoke '{action.Name}' failed.";
        const string blockedPrefix = "blocked by policy '";
        var blockedIdx = message.IndexOf(blockedPrefix, StringComparison.Ordinal);
        if (blockedIdx >= 0 && message.StartsWith($"'{action.Name}'", StringComparison.Ordinal)) {
            var start = blockedIdx + blockedPrefix.Length;
            var end = message.IndexOf('\'', start);
            if (end > start) {
                var leaf = message[start..end];
                var guard = action.Policies.Select(p => p.Name).FirstOrDefault(n =>
                    string.Equals(n, leaf, StringComparison.Ordinal)
                    || string.Equals(n, "not_" + leaf, StringComparison.Ordinal)) ?? leaf;
                return ModuleOutcome.Fail(message, [guard]);
            }
        }
        if (message.Contains("requires a linked", StringComparison.Ordinal))
            return ModuleOutcome.Fail(message, [.. action.Policies.Select(p => p.Name)]);
        return ModuleOutcome.Fail(message);
    }
}