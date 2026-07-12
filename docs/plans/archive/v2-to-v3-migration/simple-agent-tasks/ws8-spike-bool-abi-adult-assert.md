# Micro-Task: Bool ABI — adult guard fail-closed for Dict/Expando

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6e**  
**Difficulty**: Small  
**Estimated Tokens**: ~2k  
**Status**: [ ] Not Started  
**Depends on**: `ws8-spike-harden-negative-subject-tests.md` (Done)

## Objective

Adult-comparison asserts on Dictionary/Expando must fail closed if the path becomes correct and returns **`bool true`**, not only `long == 1`.

## Code review finding (post-#6b)

Current guard assert:

```csharp
bool isAdult = guardResult.Value is long l && l == 1L;
await Assert.That(isAdult).IsFalse();
```

If a fixed Dict path returns **`true`** (bool), `isAdult` stays false → **test still passes** when evaluation is correct.

## Exact Steps

1. In `PolicySampleSubjectSpikeTests`, add helper e.g. `IsVmTrue(object? v)`:
   - `v is true` **or**
   - `v is long l && l == 1` **or**
   - `v is int i && i == 1` (if observed)
2. Use `IsVmTrue` for Dict/Expando adult guards (Age=99999 or Age=25 + `Age >= 18`).
3. Optionally use `IsVmTrue` / `IsVmFalse` in positive paths instead of raw `(long)result.Value`.
4. One-line note in spike doc under hardened negatives.

## Verification

- [ ] Adult assert fails if result is `bool true` **or** `1L`
- [ ] Current Dict/Expando behavior still passes
- [ ] Spike tests green

## Out of Scope

- Subject helper (#6d)
- MCP tools
