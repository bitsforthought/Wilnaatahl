---
name: implement-plan
description: >-
  Implements an approved Wilnaatahl plan as a gh-stack, one validated and
  reviewed layer and PR per step. Use after plan approval or to resume
  a partly implemented stack plan.
---

# Implement an approved plan

Input: an approved bottom-up plan. Output: a published PR for each completed step.
You are the coordinator. Read the
[implementation lifecycle](../../../docs/agents/implementation.md) for testing,
validation, committed review, and squashing before each push.
Use [`gh-stack`](../gh-stack/SKILL.md) for stack commands and branch ownership.

## Prepare

Each step must have one reviewable concern, acceptance criteria, approved test
seams, a branch name, and an executor:

| Work                                                  | Executor                 |
| ----------------------------------------------------- | ------------------------ |
| F# core                                               | `fsharp-implementer`     |
| TypeScript, React, or Three.js                        | `typescript-implementer` |
| Other work, including instructions and infrastructure | `primary`                |

Derive missing mechanical details and show the completed step list. Obtain
missing design or seam decisions before implementing. Each step must leave
the repository green.

Run `git status --short --branch` and `gh stack view --json`. Exit 2 means a new
stack only when there is no recorded stack to resume; other errors stop preparation.
For a new stack, require a clean worktree. Use the plan's specified delivery base,
or `main` when none is specified.
Fetch `origin` and fast-forward the local base; stop on divergence or ambiguity.
An existing stack must match the plan; ask before extending an unrelated one.

Track progress by recording each step's branch, parent, PR, completion evidence,
and whether it is being implemented, awaiting review, reviewed, or published.
A commit alone does not establish review or publication.

When resuming interrupted work or publication, or repairing a lower layer, read
[recovery guidance](references/recovery.md) before proceeding.

## Complete each step

1. **Create the layer.** For the first step, use
   `gh stack init --base <delivery-base> <branch>`. For later steps, use
   `gh stack top` and `gh stack add <branch>` only after the previous PR is
   published. Record the parent and the commit range to preserve before edits.
2. **Assign the work.** For `primary`, work in this session. Otherwise dispatch
   synchronously to the named implementer with the task, acceptance criteria,
   approved seams, and the lifecycle's applicable guidance. The implementer
   owns edits and focused checks; you own state-changing Git and completion.
3. **Check the result.** Compare the report with the actual changed files and
   focused evidence. Return omissions or fixes to the same implementer.
   After two unresolved follow-ups, or a blocked report, stop and record why.
   Preserve incomplete work; do not commit it as complete.
4. **Validate and review.** Follow the shared lifecycle on the complete layer.
   Record the reviewed checkpoint and evidence, then squash unpublished
   checkpoints and verify the publication commit. Keep one owner per edit scope;
   reuse valid checks and pass evidence paths instead of copied logs.
5. **Publish.** Require earlier layers to be published, no pending in-scope
   changes, and no `needsRebase` in stack JSON. Run
   `gh stack submit --remote origin --auto --open`. Verify local/remote tip
   agreement, `origin/<branch>` tracking, PR base, OPEN state, and non-draft status.
   If submission fails or any check is missing or mismatched, read
   [publication recovery](references/recovery.md#verify-publication) before proceeding.
   Use the [`pr` skill](../pr/SKILL.md) for the body, then
   `gh pr edit --body-file`.
   Record the publication SHA, evidence, and PR URL before starting the next step.

Stop after publishing the final step. Human review responses, merging, and
cleanup are separately requested phases; do not monitor them during implementation.
Use `origin` explicitly for submit and rebase. Leave repository settings and
unrelated Git configuration unchanged. Stop on a failed check, unresolved review,
conflicting state, or unavailable stacked PR support rather than bypassing it.
Keep discoveries outside the approved plan as follow-up suggestions.
