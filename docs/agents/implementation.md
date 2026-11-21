# Implementation lifecycle

This document is for the **coordinator**: the agent running `implement-plan`,
`implement`, or `implement-spec`. Read it before starting work. You assign edits,
check the complete change, commit it, and organize its review.
An **implementer** makes assigned changes and runs focused checks. When running the
`implement` skill, you may do that work yourself instead of assigning it to another agent.

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
Record the tip to preserve: the last-pushed commit, or the starting parent for
a new branch. For a tool-managed stack repair, use the
[repair guidance](../../.agents/skills/implement-plan/references/recovery.md#repair-a-lower-layer)
to identify its rebased equivalent.
Stop if that range would include unrelated local commits.

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
5. After validation passes and review accepts the final checkpoint, prepare
   one publication commit using [Squash before pushing](#squash-before-pushing).
   Declare completion or publish only after its verification passes.
   A local commit alone does not authorize publication.

The review is required even for small or instruction-only changes.
The [review process](../../.agents/skills/adversarial-code-review/process.md)
defines model selection, rounds, and how to record findings.
Stop and report **blocked** if a required check or review remains unresolved.

## Squash before pushing

You own this step. After the panel accepts the change, combine this work's
unpublished implementation and review-fix commits into one commit. Use
[`commit-messages`](../../.agents/skills/commit-messages/SKILL.md) to describe
the whole change, not the sequence of review fixes. Reuse a single checkpoint
when there is nothing to combine.

If the reviewed checkpoint already equals the tip to preserve, there are no
unpublished checkpoints. Reuse that tip with the unchanged review base and
acceptance criteria, without squashing or creating a commit. Still complete
the publication checks; a reviewed draft or restacked branch may need readiness
or a tool-managed push.

Keep previously pushed commits unchanged during this squash. For a follow-up to
a published PR, the new commit follows the last-pushed tip; it does not replace
the whole PR. Tool-managed stack rebases remain a separate operation.
This is a local history operation, not a GitHub PR merge.
Require a clean worktree and confirm the remote tip has not changed.
Preserve unrelated work and stop on a conflicting commit range or remote update.

When unpublished checkpoints follow the preserved tip, verify before pushing:

- The publication commit has exactly the reviewed checkpoint's Git tree.
- Its parent is the recorded tip to preserve.
- The review base and acceptance criteria are unchanged.

Record the reviewed SHA, publication SHA and the applicable tip/tree checks with the
validation and review evidence. Reuse that evidence when all checks match:
only history and the commit message changed. Changed content or review base
returns to validation and review, rather than inheriting approval.

### Adapting Matt Pocock's implementation skills

Replace the automatic `code-review` step in `implement` and `implement-spec`
with this repository's multi-model review. Do not run a second automatic
Standards/Spec review. An explicit user request for the
[`code-review` skill](../../.agents/skills/code-review/SKILL.md) still runs that
skill; it does not replace the required review of newly implemented work.

For `implement`, commit **before** review. Its final commit instruction is
already satisfied by the verified publication commit if no changes remain;
do not create an empty extra commit.

For `implement-spec`, allow integration pushes and a draft PR before all work
is complete. Update the last-pushed tip after each authorized draft push; those
commits are outside the later squash range.
Keep the PR draft and tickets unresolved until the complete branch
passes validation and review. Assign accepted findings to its fresh remediation
implementer, then validate, commit, and review the fixes. Individual tickets do
not need full validation or completion panels solely because they use specialized
implementers. Integration or conflict-resolution changes need validation and
review results for the changed content.

For a validation-only request, use `validate-change` on the existing change.
Do not start implementation, seam approval, commits, or a new completion review.
