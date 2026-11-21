# Implementation lifecycle

This document is for the **coordinator**: the agent running `implement-plan`,
`implement`, or `implement-spec`. Read it before starting work. You assign edits,
check the complete change, commit it, and organize its review.
An **implementer** makes assigned changes and runs focused checks. You may do
that work yourself instead of assigning it to another agent.

These rules adapt Matt Pocock's implementation skills without changing the
vendored files.

## Choose when to complete the review

| Skill            | Review is required for                                | Who changes Git state                                                                                                                           |
| ---------------- | ----------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `implement-plan` | Each layer before publishing it and starting the next | You; implementers return uncommitted changes                                                                                                    |
| `implement`      | The complete standalone task                          | You                                                                                                                                             |
| `implement-spec` | The integration branch after all tickets are merged   | You own the integration branch; explicitly authorize ticket implementers to commit their work branches and merger agents to merge those commits |

Use the skill the user requested. An approved spec does not become a stack plan
when the user asks for `implement-spec`.

## Assign the work

Before assigning edits, identify the acceptance criteria, review base, and Git
owners in the table above. Assign each edit scope to one implementer.

Use the spec or plan's Testing Decisions and [testing standards](../testing.md).
Obtain any missing approval for test seams before test authoring. Reuse approval
for an existing seam; an implementer returns a proposed new or changed seam to you for
confirmation. `to-spec` already provides this agreement. `grill-with-docs` can
help prepare it; no separate approval document is required.

Read these references for work you do yourself; pass them to assigned implementers:

| Work                                                      | Required guidance                                                                                    |
| --------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Behavioural changes                                       | [`tdd`](../../.agents/skills/tdd/SKILL.md) and [testing standards](../testing.md)                    |
| F# source or scripts                                      | [`fsharp-style`](../../.agents/skills/fsharp-style/SKILL.md)                                         |
| F# tests                                                  | [`fsharp-testing`](../../.agents/skills/fsharp-testing/SKILL.md), including its triggered references |
| TypeScript or React source                                | [`typescript-style`](../../.agents/skills/typescript-style/SKILL.md)                                 |
| TypeScript tests                                          | [`typescript-testing`](../../.agents/skills/typescript-testing/SKILL.md)                             |
| Build, CI, coverage, code generation, or package commands | [`infra-scripts-fsharp`](../../.agents/skills/infra-scripts-fsharp/SKILL.md)                         |

Require focused tests and type checks during implementation. For changes without
executable behaviour, require the applicable instruction or document checks,
not an invented behavioural test. Implementers do not run separate completion
reviews.

Require a report marked **candidate** (ready for your checks, not complete) or
**blocked**. It must list changed files, decisions, focused commands and results,
evidence paths, and unresolved concerns. Changed behaviour needs meaningful RED
evidence. Check the report against the actual change; return omissions to the
same implementer. Pass paths, not copied logs. Reuse unchanged check results
according to [`validate-change`](../../.agents/skills/validate-change/SKILL.md#scope-and-reuse).

### Ticket branches in `implement-spec`

Keep its separate worktrees and merger agents. Require each ticket implementer
to pass its focused checks and commit its work before merging the latest integration
tip. After the merge, it must resolve conflicts, recheck the combined change,
commit fixes, and report the final commit from a clean worktree.
Merger agents merge only committed ticket work. If its worktree still has
uncommitted changes, return the ticket to its implementer. Ticket checks do not
replace validation of the complete integration branch.

## Validate, commit, and review

Apply these steps to the complete task, layer, or integration branch named above:

1. Run [`validate-change`](../../.agents/skills/validate-change/SKILL.md).
   Inspect formatting and refactoring output. Record the checked content,
   including generated changes; the old `HEAD` alone does not identify it.
2. Stage only in-scope changes, inspect the staged diff, and commit with
   [`commit-messages`](../../.agents/skills/commit-messages/SKILL.md).
   Reuse an already-committed tip instead of creating an empty commit.
   All in-scope changes must be committed before review. Preserve unrelated
   user changes without adding or discarding them.
3. Record the base and commit SHAs. Run the mandatory multi-model
   `adversarial-reviewer` panel using the
   [review process](../../.agents/skills/adversarial-code-review/process.md).
4. Assign accepted fixes and required refactoring to the responsible implementer.
   Refactoring takes place during review fixes, outside the TDD red/green loop.
   Remove dead code and its unused supporting types, keep comments accurate,
   and preserve appropriate visibility. Collect focused results, validate the
   changed content as required, and make a follow-up commit before reviewing
   the fixes and regressions they caused.
5. Declare completion or publish only when validation passes and the review
   accepts the final commit against its recorded base. A local commit alone
   does not authorize publication.

The review is required even for small or instruction-only changes.
The [review process](../../.agents/skills/adversarial-code-review/process.md)
defines model selection, rounds, and how to record findings.
Stop and report **blocked** if a required check or review remains unresolved.

### Adapting Matt Pocock's implementation skills

Replace the automatic `code-review` step in `implement` and `implement-spec`
with this repository's multi-model review. Do not run a second automatic
Standards/Spec review. An explicit user request for the
[`code-review` skill](../../.agents/skills/code-review/SKILL.md) still runs that
skill; it does not replace the required review of newly implemented work.

For `implement`, commit **before** review. Its final commit instruction is
already satisfied if no changes remain; do not create an empty commit or amend
the reviewed commit.

For `implement-spec`, allow integration pushes and a draft PR before all work
is complete. Keep the PR draft and tickets unresolved until the complete branch
passes validation and review. Assign accepted findings to its fresh remediation
implementer, then validate, commit, and review the fixes. Individual tickets do
not need full validation or completion panels solely because they use specialized
implementers. Integration or conflict-resolution changes need validation and
review results for the changed content.

For a validation-only request, use `validate-change` on the existing change.
Do not start implementation, seam approval, commits, or a new completion review.
