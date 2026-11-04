namespace Poly.Tests.Interpretation;

/// <summary>
/// Composes many small, individually proven nodes into one method body, so the differential can see a
/// history instead of a single shape. <see cref="AstParity"/> gives every row a fresh VM, an empty
/// heap and an empty ring; a sequence hands the same node kinds to the same two executors while they
/// share locals, ring depth, loop back-edges and unwind state.
/// <para>
/// The oracle is unchanged: <see cref="AstParity.Compare"/> runs the VM and the Roslyn-compiled print.
/// The only addition is <see cref="FirstDivergence"/>, which walks prefixes to name the step where the
/// two sides part, because a final-value mismatch on a ten-step body is not a diagnosis.
/// </para>
/// </summary>
public static class AstParitySequence {
    /// <summary>One statement in the body, labelled so a divergence can name it.</summary>
    public sealed record Step(string Label, Node Body);

    /// <summary>
    /// A body: the ordered steps, the locals they share, and the one local each prefix is read through.
    /// Every case seeds <paramref name="Observable"/> in its first step, so a prefix always has a value
    /// to report and the prefix walk sees the sequence's state rather than an unassigned slot.
    /// </summary>
    public sealed record SequenceCase(
        string Label,
        IReadOnlyList<Step> Steps,
        IReadOnlyList<Variable> Variables,
        Variable Observable) {
        /// <summary>The whole body as one program, the shape both executors already accept.</summary>
        public Block Program => new([.. Steps.Select(step => step.Body), Observable], Variables);

        /// <summary>The body cut after <paramref name="count"/> steps, read through <see cref="Observable"/>.</summary>
        public Block Prefix(int count) => new([.. Steps.Take(count).Select(step => step.Body), Observable], Variables);
    }

    /// <summary>Runs both executors over the whole body, or over its first <paramref name="prefixSteps"/> steps.</summary>
    public static ParityRow Compare(SequenceCase sequence, int? prefixSteps = null) {
        ArgumentNullException.ThrowIfNull(sequence);
        return prefixSteps is { } count
            ? AstParity.Compare($"{sequence.Label} [1..{count}]", sequence.Prefix(count))
            : AstParity.Compare(sequence.Label, sequence.Program);
    }

    /// <summary>
    /// The first step whose prefix the two sides dispose of differently, or null when every prefix
    /// agrees. Prefixes restart from the beginning, so a reported index is where the two sides part,
    /// not where the final value happened to land. It reports the first step the <em>observable</em>
    /// can see: a step that writes a local nothing reads until later is invisible until the step that
    /// consumes it, so read the index as the first step whose effect reaches the observable.
    /// </summary>
    public static (int Index, string Label, string Vm, string Printed)? FirstDivergence(SequenceCase sequence) {
        ArgumentNullException.ThrowIfNull(sequence);
        for (var count = 1; count <= sequence.Steps.Count; count++) {
            var row = Compare(sequence, count);
            if (!row.Agrees)
                return (count - 1, sequence.Steps[count - 1].Label, row.VmOutcome, row.PrintedOutcome);
        }
        return null;
    }

    /// <summary>The whole-body row plus the first divergent step, as one failure message.</summary>
    public static string Describe(SequenceCase sequence) {
        ArgumentNullException.ThrowIfNull(sequence);
        var lines = new List<string> { Compare(sequence).Describe() };
        if (FirstDivergence(sequence) is { } divergence) {
            lines.Add($"  first divergent step: {divergence.Index} '{divergence.Label}'");
            lines.Add($"    VM:      {divergence.Vm}");
            lines.Add($"    printed: {divergence.Printed}");
        }
        return string.Join("\n", lines);
    }
}