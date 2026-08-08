# P3-3 — MCP invoke result honesty

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** P3-2  

## Objective

MCP `invoke_action` (or equivalent) surfaces the return value when runtime has one; tool description matches. Fail closed or honest null when void.

## Required reading

- RuntimeTool / DomainTools invoke handlers  
- P3-2 golden  

## Exact steps

1. Project result value into tool response if not already.  
2. Smoke/test asserts field present for `-> T` action.  
3. Description text updated if claims change.

## Verification

- [ ] MCP smoke or unit test green  
- [ ] No second result store  

## File ownership

- Poly.Mcp tools + MCP tests  

## Status

**Status:** Not Started  
