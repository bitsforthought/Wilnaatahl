---
name: stack-cleanup
description: >-
  Cleans up a fully merged stack: protect local commits, prune its completed
  local branches and registration, and finish on the updated trunk. Use after
  every layer has merged, including a completed local-only stack registration.
---

# Clean up a fully merged stack

The user selects this workflow. Do not classify PRs or select another workflow.
Load `gh-stack` for command semantics and non-interactive requirements.

## Confirm the target and protect local work

1. Require a clean worktree with `git status --short --branch`. Never stash,
   discard, reset, or move unrelated changes.
2. Select only the requested stack through an existing local branch. Keep an
   already active matching stack; otherwise use
   `git switch --no-guess <supplied-local-branch>`. A supplied PR's head branch
   can be read with `gh pr view <number-or-url> --json headRefName`.
   If no surviving selecting branch is identified, or registration identity is
   ambiguous, stop and ask. Do not use `gh stack checkout` or reconstruct
   missing branches. Record the selected branch for unregistering later.
3. Read `gh stack view --json` and retain the exact branch list, trunk, and
   PR identities. Confirm every recorded PR is `MERGED` with `gh pr view`;
   retain each PR's `headRefOid`. Missing PRs, open PRs, and queued PRs block
   cleanup. Never infer merge state or stack numbers from PR numbers.
4. For every existing local branch slated for deletion, apply the
   [local-commit proof](../pr-cleanup/SKILL.md#protect-local-commits) against
   its own merged PR. Missing local branches stay absent. Stop if a branch
   contains unrepresented work or is checked out in another worktree.
5. Keep the cleanup set fixed to this verified stack. If its trunk is in the
   deletion set, or selecting/removing its registration would affect another
   stack, stop and ask. Do not remove unrelated branches or registrations.

## Clean up

1. Run `git fetch --prune origin`, switch to the recorded trunk, and run
   `git merge --ff-only origin/<trunk>`. Stop on divergence; do not reset or
   rebase the trunk. Keep the registration intact until this succeeds.
2. Return with `git switch --no-guess <recorded-selecting-branch>` and remove
   only its verified active registration with `gh stack unstack --local`.
   This works for GitHub-backed and local-only registrations without changing
   GitHub or needing a stack number. Stop if either command fails.
3. Switch back to the updated trunk.
4. Delete only existing branches in the verified cleanup set with
   `git branch -D <branch>`, after their individual local-commit proofs passed.

Every PR is already merged, so no remaining ancestry needs synchronization.
Do not run `gh stack sync` just to delete these local branches.

Never use bare `gh stack unstack`, which also changes GitHub. Do not delete
remote branches. Cleanup is included in the request; do not ask again whether
to prune the verified completed stack.

## Verify

Run `git fetch --prune origin` to remove stale local remote-tracking refs.
Confirm the worktree is clean on the trunk, the trunk matches `origin/<trunk>`
(`git rev-list --left-right --count <trunk>...origin/<trunk>` prints `0 0`),
the completed local branches and exact local registration are absent, and the
recorded PRs remain `MERGED`. Leave unrelated local branches unchanged and do
not restore the starting branch after final cleanup.
