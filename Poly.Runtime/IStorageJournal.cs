namespace Poly.Runtime;

/// <summary>
/// The undo log an action scope rolls a storage port back through, #677 R-08 (Q1).
/// <see cref="Mark"/> records a position in the port's journal and
/// <see cref="RollbackTo"/> reverts every change journaled after it. A host with no storage
/// passes <c>null</c> to <see cref="ActionScope.Begin"/>, and the scope then restores entity
/// state only.
/// </summary>
/// <remarks>
/// <see cref="ActionScope"/> takes exactly one root mark per live scope and keeps it until the
/// scope closes, so a port sees at most one scope-owned mark in flight. The journal is the
/// port's, not the scope's: the scope only names the position. A host that also takes its own
/// mark must take it outside the scope's, because a port reverts a flat journal and an outer
/// mark rolled back first would invalidate the scope's.
/// </remarks>
public interface IStorageJournal {
    /// <summary>This port's current journal position.</summary>
    int Mark();

    /// <summary>Reverts every journaled change recorded after <paramref name="mark"/>.</summary>
    void RollbackTo(int mark);
}