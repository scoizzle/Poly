# TASK <id> - <title>
Status: planned | in review | fix round <n> | SHIP <sha8> | merged #<N> `<sha8>` <date> | BLOCKED (see Needs Scot)
Branch `slice/<id>`. PR #<N>. Lane <A|B>. Review <1|2>. Implement mill: <Grok|OpenCode|hand> (review on the opposite mill).
Plan card: [`pipeline-convergence-plan.md` **<id>**](../../../domain-modeling/pipeline-convergence-plan.md).

## Scope
3-6 lines. Every file:line checked against the code on master.

## Files
Exact paths, tests included.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|

## Done when

## SHIP if / NOT SHIP if

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/<Class>/*'` (one line per class), then the full suite.

## Hand-edit
One line: why Scot can open the result cold and change it by hand.

## Needs Scot
none | one question

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
