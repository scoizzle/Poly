# C4-repair-adversarial — Dogfood Findings

**Date:** 2026-07-18
**Total findings:** 4 (1 OK, 3 pain)
**Completed:** True

## Findings

| ID | Cat | Title | Severity | PainScore | Bucket |
|----|-----|-------|----------|-----------|--------|
| C4-F3b | D | Missing relationship target threw exception | 3 | 13 | guide-honesty |
| C4-F4 | A | No suggestion for stage-less entity | 2 | 13 | other |
| C4-F1b | D | Lab construct rejection message unclear | 2 | 12 | guide-honesty |
| C4-F2 | X | Named policy 'require' syntax works | 0 | 0 |  |

## Details

### C4-F3b: Missing relationship target threw exception
- **Category:** D
- **PainScore:** 13 (S=3 F=1 B=3 C=3)
- **Notes:** Expected diagnostic, got FormatException: 'Poly DSL parse error at line 8, col 1: Navigation property 'visits' references unknown entity 'Appointment'. No entity with that name was found in the domain.'
- **Expected:** Better behavior
- **Actual:** Expected diagnostic, got FormatException: 'Poly DSL parse error at line 8, col 1: Navigation property 'visits' references unknown entity 'Appointment'. No entity with that name was found in the domain.'
- **Repro:**
  1. Parse '.poly' with nav to non-existent entity
- **Workaround:** Define the target entity first

### C4-F4: No suggestion for stage-less entity
- **Category:** A
- **PainScore:** 13 (S=2 F=3 B=2 C=4)
- **Notes:** Entity with properties + no stages should trigger DMAS001. No suggestion found.
- **Expected:** Better behavior
- **Actual:** Entity with properties + no stages should trigger DMAS001. No suggestion found.
- **Repro:**
  1. Create entity with properties
  1. Don't add stages
  1. Check analysis suggestions
- **Workaround:** Suggestion quality is advisory; domain still valid

### C4-F1b: Lab construct rejection message unclear
- **Category:** D
- **PainScore:** 12 (S=2 F=2 B=2 C=4)
- **Notes:** Error message was: 'Poly DSL parse error at line 4, col 7: Expected Colon, got 'Patient' (Identifier)'. Expected hint about using 'entity'.
- **Expected:** Better behavior
- **Actual:** Error message was: 'Poly DSL parse error at line 4, col 7: Expected Colon, got 'Patient' (Identifier)'. Expected hint about using 'entity'.
- **Repro:**
  1. Parse '.poly' with 'actor' keyword
- **Workaround:** Message is functional but could mention 'entity' alternative

