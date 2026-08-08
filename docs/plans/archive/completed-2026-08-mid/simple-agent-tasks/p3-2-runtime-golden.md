# P3-2 — Runtime golden for one return shape

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P3-0; soft after P3-1  

## Objective

Prove end-to-end: invoke action with `-> T` yields the declared result value on the product runtime path (DomainEntityInstance / store), not only success bool.

## Required reading

- Invoke pipeline, create effects, result members  
- P3-0 chosen shape  

## Exact steps

1. Implement minimal plumbing if missing (smallest change).  
2. Golden test: DSL or evolution domain → InvokeAction → assert result payload.  
3. Void actions remain success-only.

## Verification

- [ ] Golden green  
- [ ] No multi-hop / date work  

## File ownership

- Runtime invoke path + tests  
- **Do not edit:** guide (P3-4), MCP unless required for runtime  

## Status

**Status:** Not Started  
