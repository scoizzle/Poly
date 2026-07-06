namespace Poly.Interpretation.Vm;

/// <summary>
/// Kind of exception region handler.
/// </summary>
public enum RegionKind {
    Catch,
    Finally,
    UsingDispose
}

/// <summary>
/// A single entry in the exception region side table.
/// Maps a protected PC range to a handler delegate in <see cref="VmProgram.Functions"/>.
/// Serializable-friendly: all fields are ints, strings, or enums — no CLR type references.
/// </summary>
/// <param name="TryStartPc">Inclusive start PC of the protected region (try body).</param>
/// <param name="TryEndPc">Exclusive end PC of the protected region.</param>
/// <param name="HandlerFuncIndex">Index into <c>VmProgram.Functions</c> for the handler delegate.</param>
/// <param name="Kind">The kind of region: Catch, Finally, or UsingDispose.</param>
/// <param name="CatchTypeName">Assembly-qualified type name for catch filters; null for finally/using.</param>
/// <param name="CatchVariableName">Optional variable name binding the caught exception; null for finally/using.</param>
/// <param name="ParentRegionIndex">Index of the enclosing region, or -1 for top-level regions.</param>
public sealed record ExceptionRegionEntry(
    int TryStartPc,
    int TryEndPc,
    int HandlerFuncIndex,
    RegionKind Kind,
    string? CatchTypeName = null,
    string? CatchVariableName = null,
    int ParentRegionIndex = -1
);

/// <summary>
/// Side table attached to <see cref="VmProgram"/> for structured exception handling.
/// Maps protected PC ranges to handler delegates. Used by the dispatch expression
/// generated at compile time (Strategy B — Runtime Dispatch).
/// </summary>
/// <param name="Entries">Ordered list of exception region entries. Should be ordered
/// innermost-first for efficient dispatch scanning.</param>
public sealed record ExceptionRegionTable(
    IReadOnlyList<ExceptionRegionEntry> Entries
) {
    /// <summary>
    /// Returns a new table with the handler index updated at the specified position.
    /// </summary>
    public ExceptionRegionTable WithHandlerIndexAt(int entryIndex, int handlerFuncIndex) {
        var updated = new List<ExceptionRegionEntry>(Entries.Count);
        for (int i = 0; i < Entries.Count; i++) {
            updated.Add(i == entryIndex
                ? Entries[i] with { HandlerFuncIndex = handlerFuncIndex }
                : Entries[i]);
        }
        return new ExceptionRegionTable(updated.AsReadOnly());
    }
}