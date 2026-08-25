# Micro-Task (optional): Fail-loud bad effect token error-string smoke

**Suite:** [`qe-README.md`](qe-README.md) **#E1′′′.3**  
**Parent:** effect-surface E1′′′ residuals  
**Difficulty:** Small  
**Estimated Context:** ~5k tokens  
**Status:** `[ ]` Not Started  
**Parallel OK** anytime; do not block Q0/Q1.

## Objective

One negative test that an unsupported/bad effect token produces an error message that **includes** the allowed set (or at least `delete` and existing keywords) — locks E1′ honesty string.

## Required Reading

- Existing ApplyDsl / parser error tests
- `PolyDslParser` effect error path

## Exact Steps

1. Find current error string for bad effect keyword.
2. Add TUnit test asserting substring(s) for supported effects including `delete`.
3. Do not broaden parser surface.

## Verification

- [ ] New test green
- [ ] Suite subset green

## Output

- Test + `../agent-summaries/qe-opt-e1-bad-effect-token-test-summary.md`

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
