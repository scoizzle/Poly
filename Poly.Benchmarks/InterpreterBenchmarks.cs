// ── InterpreterBenchmarks has been removed ───────────────────────
//
// These benchmarks depended on Lowering.Lower which emitted the old
// OpCode bytecodes.  Lowering now emits µops directly.  Once the µop
// lowering covers the same ground, re-create relevant benchmarks.
//
// In the meantime, use OpcodeBenchmarks.cs for µop-level perf
// measurements.