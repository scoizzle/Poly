# P3-0 — Inventory return surface

**Difficulty:** S  
**Status:** `[ ]`

## Objective

Document what `action … -> Type` means today across parse, analysis, runtime invoke, export, and MCP — no behavior change required.

## Required reading

- Absorption § P3  
- `InvocationResult.cs`, Action parse/print, `DomainEntityInstance.InvokeAction` / result types  
- MCP `invoke_action` response shape  

## Exact steps

1. Trace `-> Type` from parser → Action / InvocationResult.  
2. Note what InvokeAction / ActionInvocationResult expose (success, stage, value?).  
3. Note MCP tool fields for invoke results.  
4. Write short inventory: `docs/plans/simple-agent-tasks/p3-inventory-notes.md` or progress notes with file:line.  
5. Name the **one** return shape for P3-2 (e.g. create-in returns Entity / Text prop).

## Verification

- [ ] Inventory names real types and the chosen vertical shape  
- [ ] No production behavior change required  

## File ownership

- Docs/notes only  

## Status

**Status:** Not Started  
