---
name: implement-plan
description: >-
  Implements an approved Wilnaatahl plan as a gh-stack: one fully validated,
  reviewed, committed layer and PR per plan step. Use after plan approval, when
  exiting plan mode to implement, or when resuming a partly implemented plan.
---

# Implement an approved plan

Apply the mandatory dev loop in `AGENTS.md` separately to every plan step. Each
step becomes one `gh stack` layer, commit, and PR.

The primary session coordinates the work and owns state-changing git and
`gh stack` commands. Each step's executor owns its full quality loop, including
adversarial review. The primary session verifies the report mechanically; it
does not re-review or overrule the executor.

## Phases

Run **Prepare → Implement → Publish**, resuming from recorded state when needed.
Stop after creating the PRs. Monitoring, review responses, and merge maintenance
are future phases; use `post-merge-stack-maintenance` only when asked.

## Plan shape

Order steps from the bottom of the stack upward. Every step needs:

- one reviewable concern that leaves the repository green;
- a `<feature-slug>/<step-slug>` branch name;
- observable acceptance criteria, including tests;
- an executor: `fsharp-implementer` for F# core work,
  `typescript-implementer` for the TypeScript/React layer, or `primary` for work
  outside those scopes, such as documentation, instructions, skills, or scripts.

A primary-owned step that changes build, CI, coverage, code-generation
infrastructure, or the package-command facade must load
`infra-scripts-fsharp`. It must also load `fsharp-style` when changing `.fs` or
`.fsx`. Its acceptance criteria must identify the repository behaviour each
self-test protects and the available native platform or shell evidence for any
portability claim; unavailable environments remain explicitly unverified.

Put foundations below their dependants. Consult the `gh-stack` stack-design
reference when the layers are unclear. If the plan lacks required details,
derive them without changing scope and show the completed step list before
starting. One step still gets one layer.

## State

Track progress in the session database, but treat git and GitHub as
authoritative:

```sql
CREATE TABLE IF NOT EXISTS stack_steps (
  position INTEGER PRIMARY KEY,
  step_id TEXT NOT NULL UNIQUE,
  branch TEXT NOT NULL UNIQUE,
  executor TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
    -- pending | implementing | committed | published | blocked
  pr_number INTEGER,
  notes TEXT
);
```

Future phases should add structured state rather than encode it in `notes`.

On every entry:

1. Run `gh stack view --json` and `git status --short --branch`.
2. If `stack_steps` is absent, treat exit code 2 from `gh stack view` as a fresh
   run and start Prepare.
3. A step is `committed` only when its branch is in the stack and contains the
   reviewed commit for that step.
4. A step is `published` only when
   `gh pr view <number> --json state,isDraft,url` reports `OPEN` and
   `isDraft: false`. Get the number from `branches[].pr.number`.
5. For an interrupted `implementing` step, show the status and diff stat. Ask
   whether to resume its changes or discard them and redispatch the executor.
   Never commit incomplete work.

Stop and ask if recorded and actual state otherwise disagree.

## Prepare

1. Require a clean worktree; do not move or discard unrelated changes.
2. Confirm `gh stack --help` works and follow the `gh-stack` skill's
   non-interactive and remote rules. Do not change git configuration.
3. Fetch `origin`, check out the trunk (`main` unless specified), and run
   `git merge --ff-only`. Stop if it cannot fast-forward. If already in a stack,
   ask whether to extend it or start another.
4. Insert the steps into `stack_steps` and `todos`, with each step depending on
   the one below it.

## Implement

Process steps in position order. For each step:

1. **Create the layer.** Run `gh stack init <branch>` for the first step, or
   `gh stack top` then `gh stack add <branch>` for later steps. Confirm the
   branch and set the step to `implementing`.
2. **Dispatch the executor synchronously.** For a `primary` step, do the work in
   the current session. Provide the task, acceptance criteria, relevant
   lower-layer context, and the parent branch as the review base. Require the
   executor to:
   - complete its full loop, including all review rounds;
   - avoid staging, committing, switching branches, and other state-changing
     git or `gh stack` commands;
   - report changed files, decisions or deviations, the final output tail for
     `npm run validate`, and review rounds, models, and finding counts;
   - for infrastructure changes, report the regression protected by each new
     self-test, distinguish repository policy from dependency behaviour, and list
     the native platform/shell checks run or explicitly left unverified;
   - report `blocked`, not `done`, if a genuine finding remains.
3. **Check the report.** Confirm it says `done`, includes the required passing
   results, and matches the worktree: no new commits and only in-scope files
   changed. Return omissions or failures to the same executor. After two
   unresolved follow-ups, or an executor-reported block, mark the step
   `blocked`, leave it uncommitted, and stop. Do not re-review or overrule the
   executor.
4. **Commit.** Stage only the reported files, including a coverage-baseline
   update, and compare the staged file list with the report. Commit with the
   `commit-messages` skill and `git commit -F`. Record the review summary in
   `notes`, set the step to `committed`, and prefer one commit per layer.

If a later step exposes a lower-layer problem, keep the fix in its owning
layer. Never switch with uncommitted changes: finish the current step or ask the
user. Then check out the owning layer, redispatch its executor using that
layer's parent as the review base, then apply Implement steps 3–4 to check and commit
the fix. Run `gh stack rebase --upstack`, return with `gh stack top`, and run
the full gate on each rebased layer. Stop on failure. Never place a lower-layer
concern above it.

## Publish

1. Run `gh stack view --json`. Require every branch in plan order, no
   `needsRebase`, and every step `committed`.
2. Run `gh stack submit --auto --open`. On exit code 9, report that stacked PRs
   are unavailable; do not change repository settings.
3. Run `gh stack view --json` again, then map branches to
   `branches[].pr.number`. For each PR, use `gh pr edit` with a session-folder
   body file describing its purpose, dependency, acceptance criteria,
   validation, and review summary. Do not include the full ledger.
4. Run `gh pr view <number> --json state,isDraft,url`. Require `OPEN`; if it is
   still a draft, run `gh pr ready <number>` and recheck. Record the number, set
   the step to `published`, and report URLs from bottom to top.

## Guardrails

- Do not merge PRs or change repository settings.
- After publication, use follow-up commits rather than amending. Let `gh stack`
  perform its own force-with-lease pushes; never force-push manually.
- Stop on blocked work, unclear scope or state, or a rebase conflict that
  requires a behavioural decision.
- Keep discoveries outside the approved plan as suggested follow-ups.
