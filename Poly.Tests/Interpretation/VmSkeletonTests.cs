// ── VmSkeletonTests has been removed ─────────────────────────────
//
// These tests depended on the old OpCode-based bytecode format and
// the interpretive VM switch.  The VM now runs exclusively on the
// compiled µop path.
//
// See InstructionOpTests.cs for µop-level tests.
//
// Full-pipeline tests (AST → µops → execution) live in
// LoweringTests.cs once lowering is ported to emit µops.