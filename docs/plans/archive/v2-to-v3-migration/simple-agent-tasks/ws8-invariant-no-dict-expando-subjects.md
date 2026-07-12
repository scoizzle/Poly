# Micro-Task: Invariant test — reject Dict/Expando at PolicyEvaluator boundary

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6h**  
**Difficulty**: Small  
**Estimated Tokens**: ~3k  
**Status**: [ ] Not Started  
**Depends on**: Prefer `ws8-invariant-policy-subject-types.md` (#6d) — or implement minimal guard here

## Objective

Spike proved Dict/Expando are unsafe. Add a **product or test-enforced guard** so future code does not pass them into `CompileVMPredicate` / `Evaluate` without noticing.

## Options (pick one)

| Option | What |
|--------|------|
| **A. Helper-only** | Subject factory throws if values come from / subject is Dict or Expando; tests call factory only |
| **B. Evaluate guard** | Optional debug/check in PolicyEvaluator or wrapper: if `entity is IDictionary` or Expando → throw `ArgumentException` with message pointing at spike doc |
| **C. Test-only contract** | Arch/test that greps or type-checks MCP evaluate path never uses Dictionary/Expando as TEntity |

Prefer **A or B** for A+.

## Exact Steps

1. Implement chosen option.
2. Test: `Evaluate`/`CompileVMPredicate` or factory throws (or test fails) for `Dictionary<string,object>` and Expando subjects.
3. Reference `spikes/policy-sample-subject.md` in exception message or XML doc.

## Verification

- [ ] Dict subject rejected or unusable on product path
- [ ] Expando rejected
- [ ] Normal record subjects still work
- [ ] Build green

## Out of Scope

- Fixing Member resolution for dictionaries (not a goal)
