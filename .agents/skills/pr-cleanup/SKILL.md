---
name: pr-cleanup
description: >-
  Cleans up a standalone merged PR: protect local commits, delete only its local
  topic branch, prune stale remote-tracking refs, and finish on the updated trunk.
  Use when the user requests cleanup of a merged PR that is not part of a stack.
---

# Clean up a standalone merged PR

The user selects this workflow. Do not discover stack membership or classify
other PRs. Verify the requested operation's prerequisites; if they contradict
the request, stop and ask rather than choosing another maintenance workflow.

## Confirm the target

1. Require a clean worktree with `git status --short --branch`. Never stash,
   discard, reset, or move unrelated changes to make cleanup proceed.
2. Run
   `gh pr view <number-or-url> --json number,url,state,headRefName,headRefOid,baseRefName`.
   Confirm the requested PR is `MERGED`; retain its URL, head branch, and head
   SHA. Read the trunk from `gh repo view --json defaultBranchRef` and confirm
   the PR's base is that trunk. Stop if its head branch is the trunk.
3. Limit cleanup to that PR's local head branch. Leave unrelated local branches
   and stack registrations untouched. If the local branch is already absent,
   do not recreate it.

## Protect local commits

Before deleting an existing local branch, record its tip and compare it with
the confirmed PR `headRefOid`. Equality proves the tip is represented by the
merged PR. Otherwise fetch `refs/pull/<number>/head` from `origin`, verify
`git rev-parse FETCH_HEAD` equals the confirmed `headRefOid`, then run
`git merge-base --is-ancestor <local-tip> <confirmed-headRefOid>`.

Exit `0` proves ancestry; `1` means the branch contains unrepresented work;
any other exit means the check failed. A failed fetch, changed PR head, or
unsuccessful proof blocks deletion: preserve the branch and ask the user.
Do not substitute a newer fetched SHA for the recorded PR head.

## Clean up

1. Complete the local-commit proof above before deleting anything.
2. Run `git fetch --prune origin`, switch to the recorded trunk, and run
   `git merge --ff-only origin/<trunk>`. Stop on divergence; never reset,
   rebase, or force-update the trunk.
3. If the topic branch exists and its proof passed, remove only that branch
   with `git branch -D <head-branch>`. Squash/rebase merges rewrite commit
   identities, so `-d` is not a reliable test of whether the PR represents
   the branch's commits. Do not remove a branch checked out in another worktree.
4. Confirm the worktree is clean on the trunk,
   `git rev-list --left-right --count <trunk>...origin/<trunk>` prints `0 0`,
   and the local topic branch is absent.

The request authorizes this local cleanup; do not ask again whether to prune
the verified branch. Finish on the trunk even if cleanup began on an unrelated
branch. `fetch --prune` removes stale local remote-tracking refs, not remote
branches; never delete a remote branch as routine cleanup.
