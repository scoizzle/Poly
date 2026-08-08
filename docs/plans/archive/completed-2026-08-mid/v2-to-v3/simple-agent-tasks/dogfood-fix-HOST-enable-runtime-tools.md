# HOST — Enable runtime MCP tools (not Poly product code)

**Suite:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Finding:** S1-B1 — `invoke_action` “currently disabled by the user”  
**Bucket:** S/M (host / agent environment)  
**Difficulty:** Small  
**Status:** `[x]` — invoke_action callable; HOST smoke passed

## Objective

Document and verify that the MCP **host** (Cursor/Claude/Grok tool UI) has **Runtime** tools enabled so dogfood can complete invoke/link paths.

## Checklist (human or agent with host access)

- [x] Confirm `invoke_action` is registered in `Poly.Mcp` (`RuntimeTool.cs`) — product side OK  
- [x] In the MCP client UI, enable the Poly MCP server and **all tool groups** including runtime  
- [x] Smoke: create session → create_instance → invoke_action on a trivial action (Counter: Value 0→1)  
- [x] Note in dogfood-README PULL that host must enable tools before S1-R / S2  

## Definition of Done

- [x] Runtime tools callable from the agent environment used for dogfood  
- [ ] S1-R unblocked (hit different product blocker B1: `require not` negation — not host)  

## Out of Scope

- Changing Poly source to “force enable” host tools  
- Redesigning tool groups in MCP SDK unless product owns that surface  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**  
