// ── OpCode enum has been removed ──────────────────────────────────
//
// The VM no longer uses a bytecode-based instruction set.  All
// execution goes through the compiled µop (micro-operation) path.
// See MicroOperations.cs for the µop record hierarchy and
// ProgramCompiler.cs for the expression-tree compilation.
//
// Lowering (Lowering.cs) now emits µops directly instead of
// bytecodes.
//
// If you need to look up the old encoding, see git history for
// this file before it was removed.