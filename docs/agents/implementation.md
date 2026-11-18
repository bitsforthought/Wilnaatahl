# Implementation lifecycle

Read this before implementing or dispatching work through `implement-plan`,
`implement`, or `implement-spec`. It owns local completion responsibilities and
explicitly adapts the implementation skills' defaults; vendor files remain unchanged.

## Agree the work

The coordinating agent identifies acceptance criteria, completion boundary,
Git-operation owners, and review base before dispatch. The coordinator may
implement directly or delegate bounded edits; only one agent owns an edit scope.

| Entry point      | Completion boundary                                    | Git ownership                                                                                                                                            |
| ---------------- | ------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `implement-plan` | Each layer before publishing it and beginning the next | Primary session; workers return uncommitted changes                                                                                                      |
| `implement`      | The standalone task                                    | Agent running the skill                                                                                                                                  |
| `implement-spec` | The assembled integration candidate                    | Coordinator owns integration branch; explicitly authorize ticket implementers to commit their work branches and merger agents to integrate those commits |

Use the spec/plan's Testing Decisions and [testing standards](../testing.md).
The coordinator obtains missing seam approval before test authoring. Changes to
an agreed seam return to the coordinator for confirmation, not a second
interview by a worker. `to-spec` already supplies seam agreement; `grill-with-docs`
can inform it. Do not invent a separate approval artifact.

Choose the requested entry point: a supplied spec executed with `implement-spec`
does not become a stack just because it has an approved design. Use
`implement-plan` for an approved stack plan.

## Implement a candidate

Apply the `tdd` skill with the local testing standards and relevant language
skill. Run focused tests and type checks during iteration. Specialized
implementer agents own edits and these checks, not independent completion panels.
For changes without executable behaviour, use applicable instruction/document
checks rather than inventing a behavioural test.

A worker reports **candidate** or **blocked**, with changed files, decisions,
focused commands/results and evidence paths (including meaningful RED for changed
behaviour), and unresolved concerns. Keep handoffs sparse and pass references,
not duplicated logs. The coordinator checks the report against the actual scope;
return omissions to the same worker. Reuse valid evidence rather than rerunning
checks merely because a different agent reads the report.

For `implement-spec`, each ticket implementer runs focused checks and commits
its ticket checkpoint before merging the latest integration tip. Resolve any
conflicts, recheck the combined candidate, commit any remaining resolutions or
fixes, and report the final commit from a clean worktree. Preserve `implement-spec`'s
isolated-worktree and merger roles. A merger integrates only committed ticket
work; return a dirty handoff to its owner. Ticket checks do not replace the
integrated gate.

## Complete the boundary

The coordinator performs this sequence on the entire completion candidate:

1. Apply `validate-change`. Inspect any formatting/refactoring output before
   accepting the result; identify the tested content, not merely its old `HEAD`.
2. Stage only in-scope changes, inspect the staged diff, and commit using
   `commit-messages`. If the complete candidate is already committed, reuse its
   tip rather than create an empty checkpoint. A local candidate commit is a
   review checkpoint, not completion. Require all in-scope changes to be
   committed before review; preserve unrelated user changes without including
   or discarding them.
3. Pin base and candidate SHAs and coordinate the `adversarial-reviewer` panel
   using its [review process](../../.agents/skills/adversarial-code-review/process.md).
4. Assign accepted fixes and justified refactoring to the responsible implementer.
   Refactoring is outside the TDD red/green loop, during review remediation.
   Keep dead-code removal deep, comments truthful, and encapsulation appropriate.
   Collect focused evidence, validate the revised candidate as required, then
   make a follow-up commit before the next delta review.
5. Declare completion or publish only with passing validation and resolved review
   evidence for the final candidate and review base.

The panel is mandatory regardless of perceived risk, including instruction-only
changes. Its process owns model diversity, rounds and the finding ledger.
On an unresolved failure, stop and report blocked rather than declaring success.
Use `validate-change` for evidence validity and reuse after fixes.

### Adapting Matt's implementation skills

In this repository, the automatic `code-review` call in `implement` and
`implement-spec` is fulfilled by the owned completion panel above, **instead of**
a second automatic Standards/Spec review. Do not invoke that skill merely
to satisfy the automatic step. An explicit user request for Matt's `code-review`
still invokes that skill with its own inputs and output; it does not remove the
mandatory completion requirement for newly implemented work.

For `implement`, commit the candidate **before** its review stage. Its final
"Commit your work" instruction is already satisfied when no changes remain;
do not make an empty commit or amend the reviewed checkpoint.

For `implement-spec`, integration pushes and the draft PR may precede
completion; they are work-in-progress, not accepted publication. Keep the PR a
draft and tickets unresolved until the full gate and panel accept the assembled
candidate. Its fresh remediation implementer fixes the panel's accepted findings;
the coordinator then validates, commits, and obtains the required delta review.
No per-ticket completion panels or full gates are required solely because a
ticket uses a specialized implementer. Any integration, conflict resolution, or
fix changing the final candidate requires applicable validation/review evidence
for that new content.

Validation-only requests use `validate-change` on the existing candidate; they
do not initiate TDD, a seam interview, a commit, or a new completion review.
