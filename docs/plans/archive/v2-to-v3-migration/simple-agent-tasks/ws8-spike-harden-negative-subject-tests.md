# Micro-Task: Harden policy sample-subject negative tests

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6b**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [x] **Done** — `MatchNumeric` (int/long/short/byte/…); property identity + Age≥18 not adult-true (`long == 1`).  
**Last review**: 2026-07-11 accepted A−; residual bool ABI → **#6e**

## Objective

Negative spike tests for Dictionary/Expando must **fail closed** if those bags ever start returning the **correct** property/policy results.

## What landed

- `MatchNumeric` for multi-width integer ABI
- Dict/Expando: `Property("Age")` with 99999 must not match stored value
- Dict/Expando: `Age >= 18` must not be adult-true as `1L`

## Residual (tracked elsewhere)

| Item | Task |
|------|------|
| Adult true as **`bool true`** not only `1L` | [`ws8-spike-bool-abi-adult-assert.md`](ws8-spike-bool-abi-adult-assert.md) **#6e** |
| Prove MatchNumeric true on working subject | [`ws8-spike-matchnumeric-positive-control.md`](ws8-spike-matchnumeric-positive-control.md) **#6f** |

## Verification (closed for #6b scope)

- [x] int + long covered for property identity
- [x] Adult comparison not `1L` on Dict/Expando
- [x] Tests pass on current unsafe behavior
