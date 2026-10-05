# e2e-r-9 — MCP JSON values typed

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** r-2  
**Fleet:** P1-2  

## Objective

`create_instance({"Qty":"not-a-number"})` rejected. Fractional `29.99` on Number is not truncated to `29`.

## Exact steps

1. MCP smoke tests: wrong-typed create rejected; fractional numeric preserved.
2. Coerce-or-reject at the tool boundary against property/param CLR type (same types export factories use).
3. No new MCP tool. Unified `create_instance` / invoke args only.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**` create_instance / invoke JSON bind | Domain IR |
| MCP tests | session lock (mut-safety) |

## Status

**Status:** Not Started  
**Claimed by:**  
