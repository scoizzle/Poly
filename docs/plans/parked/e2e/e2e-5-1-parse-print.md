# e2e-5-1 — Parse + print `value { }`

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** 5-0  

## Objective

Parser accepts the guide example. Printer emits it. Flip `Unsupported_ValueType_ThrowsPhase1Error` (or replace it).

## Exact steps

1. Failing round-trip test for the guide snippet.  
2. Parse → existing `ValueType` + `AddValueTypeChange`. Print walks ValueType like EnumType.  
3. Do not invent a second value-type IR.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `PolyDslParser.cs`, `DomainDslPrinter.cs`, grammar if needed | MCP (5-2) |
| `Poly.Tests/DomainModeling/Parsing/PolyDslRoundTripTests.cs` | contracts |

## Status

**Status:** Not Started  
**Claimed by:**  
