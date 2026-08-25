# Micro-Task: Multi-property sample subject for MCP evaluate_policy

**Suite:** [`vs-README.md`](vs-README.md) **#pm2-1**  
**Parent:** Post-M2  
**Difficulty:** Small–Medium  
**Estimated Context:** ~6k tokens  
**Status:** [x] Done — McpSubjectBag with 8 properties; JSON properties arg; backward-compat Age; 3 new MCP smoke tests  

## Objective

`evaluate_policy` today only accepts `age: int` and builds `EvaluationSubject(Age)`. That is **honest for Person Adult** but cannot evaluate policies on `Total`, `Status`, etc. Extend sample subject construction without inventing free-form AST bags.

## Required Reading

- `Poly.Mcp/Tools/V3DomainTools.cs` — `EvaluatePolicy`, `EvaluationSubject`
- `Poly.Tests/TestHelpers/PolicyTestSubjects.cs` if present
- `PolicySubject` product rules (no Dict as VM subject)

## Exact Steps

1. Design constrained sample args: flat map of property name → value (JSON object or repeated key/value pairs), **or** generate a CLR bag via proven helper (not raw Dictionary as evaluate target).
2. Prefer validating sample keys against **entity property names** when domain entity is available.
3. Keep Age-only overload or default for backward compatibility if agents already use `age`.
4. Tests: evaluate policy on non-Age property true/false; Age path still works; Dict path rejected if exposed.
5. Update tool Description + README honestly.

## Verification

- [ ] Non-Age property policy evaluates correctly on MCP path  
- [ ] Age Adult path still green  
- [ ] Full suite green  
- [ ] No free-form AST  

## Output

- MCP tools + tests + README  
- Summary  

## Out of Scope

- Full entity instance runtime model  
- Effect execution  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes:**
