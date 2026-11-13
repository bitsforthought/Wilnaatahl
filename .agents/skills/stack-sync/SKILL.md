---
name: stack-sync
description: >-
  Synchronizes remaining layers after lower PRs in a stack merge, including
  coordinated rebase conflict recovery. Use for a partially merged stack;
  do not prune merged branches or perform final-stack cleanup.
---

# Synchronize a partially merged stack

The user selects this workflow. Do not classify PRs or search for another
maintenance workflow. Load `gh-stack` for command semantics and non-interactive
requirements; use `--remote origin` for synchronization and rebase commands.

## Confirm the target

1. Require a clean worktree with `git status --short --branch` and record the
   starting branch. Never stash, discard, reset, or move unrelated changes.
2. Select only the requested stack through an existing local branch. Keep an
   already active matching stack; otherwise use
   `git switch --no-guess <supplied-local-branch>`. A supplied PR's head branch
   can be read with `gh pr view <number-or-url> --json headRefName`.
   If no surviving selecting branch is identified, stop and ask. Do not use
   `gh stack checkout` or reconstruct missing branches, and do not remove a
   conflicting registration.
3. Run `gh stack view --json`. Confirm it identifies the requested chain and
   retain the trunk and branches in stack order, including PR identities and
   merge/queue states. Require merged lower layers and at least one unmerged
   layer. A queued PR is not merged. If this contradicts the request, stop.

## Synchronize

1. Confirm every remaining branch has an existing local ref with
   `git show-ref --verify refs/heads/<branch>`; missing refs block synchronization.
   Select the first unmerged branch with `git switch --no-guess <branch>`.
   Use the recorded stack order, not PR numbers.
2. Run `gh stack sync --remote origin` without `--prune`. It handles rewritten
   ancestry after squash/rebase merges and updates the remaining branches and
   PR bases together. Do not manually rebase each branch or duplicate merged
   changes.
3. If sync exits `3`, it has restored the pre-sync branch state; there is no
   conflicted rebase to continue. Run `gh stack rebase --remote origin` to
   recreate a resumable rebase. Preserve the new upstream version (trunk for
   the first remaining layer, then the rebased predecessor) and retain each
   layer's own delta. Stage resolved paths and use `gh stack rebase --continue`.
   Use `gh stack rebase --abort` if resolution must be abandoned. After
   successful recovery, rerun `gh stack sync --remote origin`.
4. Treat other errors, ambiguous state, or a reported `Sync aborted` as a stop,
   even if the command exits `0`. Do not force-push manually, discard metadata,
   or try cleanup instead.

## Verify

Confirm the worktree is clean, `gh stack view --json` has the expected remaining
chain with no `needsRebase`, and each remaining branch has
`git rev-list --left-right --count <branch>...origin/<branch>` equal to `0 0`.
Verify the first unmerged PR targets the trunk and every later PR targets its
immediate remaining predecessor.

If the recorded starting branch was outside this stack and still exists,
restore it without rebasing or otherwise modifying it. Leave merged local
branches and the registration in place; this workflow does not prune or
perform final cleanup.
