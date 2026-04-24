namespace Poly.Data.Modeling;

public sealed class Stage {
    public string Name { get; set; } = string.Empty;
    public IReadOnlyCollection<Rule> Rules { get; init; } = [];
    public IReadOnlyCollection<Action> Actions { get; init; } = [];
    public Stage? SuperStage { get; init; }
    public IReadOnlyCollection<Stage> SubStages { get; init; } = [];

    public IEnumerable<Action> GetEffectiveActions() {
        var ancestry = new Stack<Stage>();
        var current = this;
        while (current is not null) {
            ancestry.Push(current);
            current = current.SuperStage;
        }

        while (ancestry.Count > 0) {
            foreach (var action in ancestry.Pop().Actions) {
                yield return action;
            }
        }
    }
}