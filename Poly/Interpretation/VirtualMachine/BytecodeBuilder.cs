// ── BytecodeBuilder has been removed ─────────────────────────────
//
// Lowering now emits µops directly (see Lowering.cs) via a
// List<MicroOp>.  There is no intermediate bytecode format.
//
// See Lowering.cs for the new EmitContext and MicroOperations.cs
// for the available µop records.