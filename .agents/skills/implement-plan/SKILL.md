---
name: implement-plan
description: >-
  Implements an approved Wilnaatahl plan as a gh-stack, one validated and
  reviewed layer and PR per step. Use after plan approval or to resume
  a partly implemented stack plan.
---

# Implement an approved plan

Read the [implementation lifecycle](../../../docs/agents/implementation.md).
The primary session coordinates each layer's validation, commits, and review.
Executors own assigned edits and focused checks. Use `gh-stack` for commands and
stack design; publish each accepted layer before starting the next.

## Plan and prepare

Each bottom-up step needs one reviewable concern, observable acceptance criteria
and approved test seams, a `<feature-slug>/<step-slug>` branch, and an executor:
`fsharp-implementer`, `typescript-implementer`, or `primary` outside their scopes.
Each step must leave the repository green. Derive missing mechanical details
without changing scope and show the completed step list; obtain missing design
or seam decisions before implementation.

Primary-owned build/CI/coverage/codegen/facade work loads `infra-scripts-fsharp`,
and `fsharp-style` for `.fs`/`.fsx`. Acceptance criteria name the regression each
self-test protects and available platform/shell evidence; mark unavailable
environments unverified.

On entry run `gh stack view --json` and `git status --short --branch`.
Exit code 2 with no recorded stack means fresh work. Otherwise reconcile recorded
state with Git/GitHub before continuing. For a fresh run:

1. Require a clean worktree; preserve unrelated changes. Confirm
   `gh stack --help`. Use `origin` explicitly for submit/rebase, without changing
   global settings, remotes, or push defaults.
2. Select the user/plan's delivery base; otherwise use `main`. Fetch `origin`,
   check out that base, and fast-forward from its remote-tracking branch.
   Stop on divergence or missing/ambiguous base. If a stack already exists,
   ask whether to extend it or start another; do not silently adopt it.
3. Record the delivery base and ordered steps in session state and todos, with
   each step depending on the one below it. The first PR targets that base,
   not an assumed `main`.

## State and recovery

Keep session-local state; Git/GitHub establish contents and publication, not
review approval:

```sql
CREATE TABLE IF NOT EXISTS stack_steps (
  position INTEGER PRIMARY KEY,
  step_id TEXT NOT NULL UNIQUE,
  branch TEXT NOT NULL UNIQUE,
  executor TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  pr_number INTEGER,
  review_base TEXT,
  candidate_sha TEXT,
  accepted_sha TEXT,
  evidence_path TEXT
);
```

`review_base` pins the parent tip; the review packet also records the actual
merge-base. `evidence_path` points to validation, panel, and ledger records.

| State          | Required evidence / next action                                                            |
| -------------- | ------------------------------------------------------------------------------------------ |
| `pending`      | No layer implementation begun                                                              |
| `implementing` | Assigned edits/focused checks in progress                                                  |
| `candidate`    | Branch tip equals `candidate_sha`; completion review still pending                         |
| `reviewed`     | Tip equals both candidate and accepted SHAs; evidence accepts this parent/base and content |
| `published`    | Reviewed candidate also has an OPEN, non-draft PR and verified `origin/<branch>` upstream  |
| `blocked`      | Failure or unresolved finding recorded; no automatic publication                           |

Use the table on resume:

- Inspect an interrupted `implementing` step's status/diff and ask whether to
  resume its edits. Do not discard changes or commit incomplete work.
- A `candidate` commit resumes completion validation/review as needed, never
  publication. Reconcile a recorded tip mismatch rather than reset the branch.
- Reuse `reviewed` evidence only when the branch tip, pinned parent, validation
  and complete panel records still match. Changed parent/content invalidates
  acceptance; return to candidate completion.
- Repair a missing upstream for a published PR without resubmitting it.
- If an old table lacks columns, add nullable evidence columns with `ALTER TABLE`.
  A legacy `committed` state is not proof of review: accept it as `reviewed` only
  after verifying its recorded candidate/base and full evidence, otherwise record
  `candidate` and obtain the missing checks/review. Stop and ask on other
  unexplained state or branch/PR mismatches.

For each PR use `gh pr view <number> --json state,isDraft,url`; read its number
from `branches[].pr.number` in `gh stack view --json`.

## Implement and complete each layer

1. **Create.** First layer:
   `gh stack init --base <delivery-base> <branch>`. Later layers:
   `gh stack top`, then `gh stack add <branch>`, only after the previous layer
   is published. Confirm the branch, pin its parent tip, set `implementing`.
2. **Dispatch synchronously.** For `primary`, edit in this session. Otherwise
   send the task, acceptance criteria, approved seams and context pointers to the
   executor. Require the lifecycle's candidate report and prohibit state-changing
   Git/stack commands. Infrastructure reports also identify each self-test's
   protected regression and platform evidence.
3. **Check the handoff.** Match files, decisions and focused-check evidence to
   the worktree; ensure no unauthorized commits. Return omissions to the same
   executor. After two unresolved follow-ups or an executor block, mark blocked,
   leave incomplete edits uncommitted, and stop.
4. **Complete as coordinator.** Apply the shared lifecycle: validation, local
   candidate commit, mandatory review, and assigned fixes/refactoring followed
   by validation/commit/delta review. Update `candidate_sha` and state `candidate`
   after every checkpoint commit; only set `accepted_sha` and `reviewed` once
   final evidence is complete. Use `commit-messages` and `git commit -F`, staging
   only in-scope changes, including any authorized baseline change.
5. **Publish** this layer using the procedure below, before creating its successor.

Keep one owner per edit scope. Reports and review packets pass paths/decisions,
not duplicated logs; do not repeat already valid checks simply for a new report.

## Publish

1. Verify the step is `reviewed` with matching base, branch tip and evidence;
   earlier layers must be published. Require no in-scope pending edits.
   Run `gh stack view --json` and require no `needsRebase`.
2. Run `gh stack submit --remote origin --auto --open`. Exit 9 blocks progression:
   report stacked PRs unavailable without changing repository settings.
3. Read stack JSON again to find the PR; reconcile interrupted submission against
   existing PRs rather than creating duplicates. Verify upstreams as below.
4. Apply the `pr` skill to a session-file body and use `gh pr edit --body-file`.
   Include purpose/dependency, acceptance criteria, validation and review summary,
   not the ledger. Say human feedback is addressed after the complete stack is
   implemented and published.
5. Verify the expected PR base, OPEN state and `isDraft: false` (use `gh pr ready`
   if needed). Mark published and report the URL. Only then begin the next layer.

Do not poll for human review, act on PR comments, or monitor lower-layer merges
during implementation. Stop after publishing the last layer. Review responses,
merging, and cleanup are separately requested phases.

### Upstream tracking

For each submitted branch, inspect
`git config --get branch.<branch>.remote` and
`git config --get branch.<branch>.merge`. If both exit 1, set
`git branch --set-upstream-to=origin/<branch> <branch>`.
If both succeed, require `origin` and `refs/heads/<branch>`; if only one is unset,
values differ, or another error occurs, stop rather than overwrite configuration.
Verify `git rev-parse --abbrev-ref --symbolic-full-name "<branch>@{upstream}"`
returns `origin/<branch>`. Upstream repair does not require another push.

## Lower-layer repairs

Keep a discovered lower-layer defect in its owning branch. Never switch with
uncommitted edits: finish the current work or ask the user. Check out the owner,
dispatch its fix, then complete it against its parent using the shared lifecycle.
Do not publish the repaired layer separately while descendants still need repair.

Before rebasing, record the affected suffix in a session-local repair record.
Run `gh stack rebase --upstack --remote origin`. For every rebased descendant,
invalidate old accepted SHAs and complete its new candidate against its new parent
with validation and review; changing the combination can change behaviour.
Use pinned before/after evidence for a scoped delta review when valid.
Stop on conflict or failed gate/review. Keep the affected suffix unpublished until
all layers are accepted, then submit it with the explicit remote, verify upstreams
and PR bases/readiness, update changed bodies, and mark the repaired steps
published.

On resume with an open repair record, reconcile actual refs and restore the
unfinished suffix checks before normal publication; do not rebase already
reconciled commits blindly. Without a recorded repair, a reviewed lower layer
with descendants is not an ordinary publish: reconcile the repair and affected
acceptance first. Never move a lower-layer concern to the top just for convenience.

## Guardrails

- Never merge PRs or change repository settings in this workflow.
- Use follow-up commits after publication, not amendments. Let `gh stack` perform
  its own force-with-lease operations; never force-push manually.
- Stop on unclear scope, recovery mismatch, rebase conflict, or genuine findings
  unresolved within the review process's round limit.
- Keep discoveries outside the approved plan as suggested follow-ups.
