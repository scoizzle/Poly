# Workstream WS4: Trace & Agent Experience Quality (Rollback UX)

**Phase**: 1  
**Priority**: High  
**Owner**: TBD  
**Status**: Not Started (file created as skeleton)  
**Last Updated**: 2026-06-01

## Goal
Ensure that the traces and rollback behavior produced by the evolution layer are high-quality, clear, and genuinely useful for LLM/MCP agents — at least as good as (ideally better than) the old V2 `ApplyWithTrace` experience.

## Entry Criteria
- WS1 has basic trace generation in place (even if rough).
- WS3 has several operations implemented that can be used for testing traces.

## Key Areas to Improve

- Richness and readability of `EvolutionTrace` and `EvolutionStep`
- Quality of diagnostics when rollback occurs
- Consistency of step descriptions across different operation types
- Usefulness for agents trying to understand "what just happened and why did it fail?"
- Performance characteristics of trace generation (should not be expensive)

## Suggested Initial Tasks (to be turned into micro-tasks)

1. Define a good `EvolutionStep` format and description convention.
2. Improve the rollback path to produce clear, actionable error information.
3. Add support for attaching relevant code snippets or context to steps where helpful.
4. Create good test coverage for trace output in both success and rollback scenarios.
5. Document "how agents should interpret traces" for future users.

## Exit Criteria
- Traces from complex multi-step evolutions (including rollbacks) are clear and actionable.
- At least one real roadblock scenario produces excellent trace output.
- Clear guidelines exist for what good vs. poor trace output looks like.
- Integration with WS5 (proof on examples) is clean.

## Dependencies
- WS1 (core trace generation)
- WS3 (operations to exercise the traces)

## Parallelism Notes
This work can start as soon as WS1 has basic trace output. It can run in parallel with further operation implementation in WS3. Good candidates for smaller agents once the core trace shape is stable.

## Related Documents
- `docs/decisions/2026-05-31-evolution-layer-design.md` (Core Contract to Preserve section)
- Future decision on trace step naming / format (likely to come out of this work)