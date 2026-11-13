namespace Poly.Grammar;

/// <summary>
/// One top-level print of a pattern row, as <see cref="PrintTrace"/> records it. Observation
/// only: nothing in the engine reads it back. Any print entry point may record one.
/// <see cref="Printer{TToken,TTokenKind}"/>'s public <c>Print</c> does today. A second entry
/// point records one too, with the same fields.
/// </summary>
/// <param name="Grammar">The table the row was printed from. A checker partitions observations by it.</param>
/// <param name="Rule">The rule that owns the row; null when the printed pattern is not a row of <paramref name="Grammar"/>.</param>
/// <param name="Pattern">The row's pattern name.</param>
/// <param name="FillKeys">The named fills the caller supplied, sorted ordinally.</param>
/// <param name="ContentEmitted">One entry per content slot the printer visited, in element order:
/// true when that slot wrote anything. Its count is the number of slots the printer consumed.</param>
/// <param name="SuppliedContentCount">How many positional content values the caller supplied, when the
/// caller knows (the C# writer's <c>RenderFills</c>); null when the caller fills slots by callback.</param>
/// <param name="Text">The printed text.</param>
internal sealed record PrintObservation(
    object Grammar,
    string? Rule,
    string Pattern,
    IReadOnlyList<string> FillKeys,
    IReadOnlyList<bool> ContentEmitted,
    int? SuppliedContentCount,
    string Text);

/// <summary>
/// An opt-in observer of top-level pattern prints, scoped to the async flow that began it.
/// Nothing records while no scope is active, so printing is unchanged outside a scope.
/// The grammar change-protocol test begins a scope, drives the printers, and checks every
/// observed row against its table (<c>docs/CORE.md</c> §3.1, grammar change protocol).
/// </summary>
internal static class PrintTrace {
    private static readonly AsyncLocal<Scope?> Active = new();

    /// <summary>True when a scope is collecting in this async flow.</summary>
    internal static bool IsActive => Active.Value is not null;

    /// <summary>
    /// Begins collecting in this async flow and the flows it starts. Dispose restores the
    /// previous scope. Begin it in the outermost scope of the drive, so every flow the drive
    /// starts inherits it.
    /// </summary>
    internal static Scope Begin() {
        var scope = new Scope(Active.Value);
        Active.Value = scope;
        return scope;
    }

    /// <summary>Adds an observation to the active scope; a no-op without one.</summary>
    internal static void Record(PrintObservation observation) {
        ArgumentNullException.ThrowIfNull(observation);
        Active.Value?.Add(observation);
    }

    /// <summary>A collecting scope. Its list is safe to add to from parallel flows.</summary>
    internal sealed class Scope : IDisposable {
        private readonly Scope? _previous;
        private readonly Lock _gate = new();
        private readonly List<PrintObservation> _observations = [];
        private bool _disposed;

        internal Scope(Scope? previous) => _previous = previous;

        /// <summary>A snapshot of the observations recorded so far, in record order.</summary>
        public IReadOnlyList<PrintObservation> Observations {
            get {
                lock (_gate)
                    return [.. _observations];
            }
        }

        internal void Add(PrintObservation observation) {
            lock (_gate) {
                if (!_disposed)
                    _observations.Add(observation);
            }
        }

        public void Dispose() {
            lock (_gate) {
                if (_disposed)
                    return;
                _disposed = true;
            }
            if (ReferenceEquals(Active.Value, this))
                Active.Value = _previous;
        }
    }
}