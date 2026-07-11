# Micro-Task: Enforce policy subject type invariants

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6d**  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~5k  
**Status**: [x] **Done** — upgraded from ban to `PolicySubject.FromDictionary()` facade using Reflection.Emit. `Validate()` now points users to `FromDictionary()` instead of blocking entirely. 22 subject-related tests pass including raw dict wrappers.

## Objective

Encode spike findings as **invariants** in code (helper + tests) so MCP/`evaluate_policy` cannot silently use Dictionary/Expando or null `int?` subjects.

## Invariants (from spike)

1. **Forbidden product subjects:** `Dictionary<string, object>`, `ExpandoObject` (and similar dynamic bags) — Member resolution does not map to keys.
2. **Forbidden:** nullable value-type properties with **null** (VM unbox failure).
3. **Allowed:** types with real CLR properties (records, StrictBag-style bags, Emit-generated types if proven).
4. **Missing keys:** map to non-null defaults (0, `""`, false) rather than null nullables when building samples.

## Exact Steps

1. Add a small helper in DomainModeling (preferred) or MCP-local mapper, e.g. `PolicySampleSubject` / `PolicySubjectFactory`:
   - Input: property name → value (and optionally entity property types from domain)
   - Output: object suitable for `PolicyEvaluator`
   - Implementation: start with **proven** StrictBag-style or reflection onto a known bag type — **not** Dict/Expando
2. Reject or throw clear `ArgumentException` if caller tries to evaluate with a Dictionary/Expando subject (if API accepts `object` subject).
3. Tests:
   - Building from bag with Age=25/15 evaluates correctly via helper + policy
   - Null nullable path either refused at build time or documented throw
4. Document one-liner on helper: “Do not pass Dictionary/Expando.”
5. `ws8-mcp-evaluate-policy-vm` should call this helper (note in that task).
6. Coordinate with **#6h** (reject Dict/Expando at boundary) — helper may own the throw, or Evaluate wrapper.
7. Coordinate with **#6g** (property name alignment) if building from entity shape.

## Verification

- [ ] Helper exists and is used by at least one test
- [ ] Dict/Expando not used on product path
- [ ] Non-null defaults for missing primitive keys
- [ ] Build green

## Related invariant tasks

- [`ws8-spike-bool-abi-adult-assert.md`](ws8-spike-bool-abi-adult-assert.md) (#6e)
- [`ws8-spike-matchnumeric-positive-control.md`](ws8-spike-matchnumeric-positive-control.md) (#6f)
- [`ws8-invariant-policy-property-name-alignment.md`](ws8-invariant-policy-property-name-alignment.md) (#6g)
- [`ws8-invariant-no-dict-expando-subjects.md`](ws8-invariant-no-dict-expando-subjects.md) (#6h)

## Out of Scope

- Full Reflection.Emit generator (unless proven)
- MCP tool wiring (use helper from #8)
