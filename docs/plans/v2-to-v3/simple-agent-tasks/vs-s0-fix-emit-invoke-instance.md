# Micro-Task: Fix instance CLR EmitInvoke (include receiver)

**Suite:** [`vs-README.md`](vs-README.md) **#0.4**  
**Parent:** Slice 0  
**Difficulty:** Small–Medium  
**Estimated Context:** ~6k tokens  
**Status:** [ ] Not Started  

## Objective

Instance method calls via `Invoke(Member(...))` on the VM path must evaluate the **receiver** before the call (today `instanceExpr` may be built but dropped from the returned expression tree).

## Required Reading

- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` — method `EmitInvoke` only (search `EmitInvoke`)
- One existing dual-oracle helper if present: `Poly.Tests/Interpretation/VmCorrectnessTests.cs` or `DirectVmAbiEmitterTests.cs`

**Do not** rewrite the whole emitter.

## Exact Steps

1. Open `EmitInvoke`. Confirm `instanceExpr` is computed for instance methods but not included in `return Block(argExprs.Concat(...))`.
2. Fix: sequence `instanceExpr` (then args, then call/result) in the returned `Block`.
3. Add dual-oracle (or VM vs expected) test:
   - e.g. `Invoke` on `"ab".Length` / `get_Length` **or** a tiny test type instance method returning a known int
4. Optional: one static method call still works (regression).

## Verification

- [ ] New test green
- [ ] Existing VM/closure invoke tests green
- [ ] Build green
- [ ] No new opcodes; no domain-specific emitter branches

## Output

- `DirectVmAbiEmitter.cs`
- New/extended test under `Poly.Tests/Interpretation/`
- Summary

## Out of Scope

- TypeCast/TypeAs/Using fixes
- Full CLR interop matrix

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
