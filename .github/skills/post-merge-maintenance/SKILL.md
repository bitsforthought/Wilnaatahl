---
name: post-merge-maintenance
description: >-
  Cleans up after a standalone PR or the final layer of a stack is merged, and
  synchronizes remaining layers after a lower stack PR merges. Use whenever a
  merged PR needs local branch cleanup or stack maintenance.
---

# Maintain a merged PR or stack

Use the shortest path that matches the merged PR. A standalone PR needs local
branch cleanup, not stack synchronization. A partially merged stack needs
synchronization but not pruning. A fully merged stack needs local cleanup.

Squash and rebase merges put rewritten commits on the trunk rather than
preserving the merged branch's original commit identities. For a stack with
remaining layers, use `gh stack sync`, which recognizes merged PRs and adjusts
the remaining ancestry; do not manually rebase each branch or duplicate
already-merged changes.

## Classify the merged PR

1. Require a clean worktree with `git status --short --branch`. Do not stash,
   discard, or move unrelated changes. Record the starting branch.
2. Confirm the PR with
   `gh pr view <number> --json state,mergedAt,mergeCommit,headRefName,headRefOid,baseRefName,url`
   and verify `state` is `MERGED`. Retain the PR number, URL, head branch, head
   SHA, base branch, and merge commit.
3. Check whether the PR has a GitHub stack using the GraphQL
   `pullRequest.stack` field. Substitute the repository owner/name and PR
   number in:

   ```bash
   gh api graphql -f query='query { repository(owner:"OWNER",name:"REPO") { pullRequest(number:PR) { stack { number } } } }'
   ```

   The query was verified against PR #65 in this repository and returned
   `stack: null`. A `null` stack means it is not currently in a GitHub stack;
   an API error or missing field is not evidence of no stack. Do not infer a
   stack number from a PR number.

4. Check the repository-local registration at
   `git rev-parse --git-path gh-stack`. This is read-only classification: parse
   the JSON and match the merged PR's `headRefName` to a branch entry. Do not
   edit this file. If it is missing or cannot be read, use GitHub stack state
   and the local branch list; if registration state remains ambiguous, stop and
   ask rather than assuming the PR is standalone.
5. Only when the PR has neither a GitHub stack nor a local registration,
   determine whether it has active dependent PRs. First list open PRs based on
   the merged head branch:
   `gh pr list --state open --base <headRefName> --limit 1000 --json number,headRefName,headRefOid`.
   Check whether the merged head branch still exists on GitHub with
   `git ls-remote --exit-code origin refs/heads/<headRefName>`; distinguish
   exit 2 (ref absent) from authentication/network errors. If it exists, the
   first query finds direct dependants. If it is absent, GitHub may have
   retargeted dependent PRs to the trunk. In that case, list open PRs based on
   the trunk with
   `gh pr list --state open --base <trunk> --limit 1000 --json number,headRefOid`.
   For each result other than the merged PR, fetch
   `refs/pull/<dependent-number>/head` from `origin` and run
   `git merge-base --is-ancestor <confirmed-headRefOid> FETCH_HEAD`. A
   successful check means the PR still contains the merged layer and is a
   dependant; exit 1 means it does not, and any other error means the check
   failed. If either PR list reaches 1000 results, or any query, fetch, or
   ancestry check fails, stop and ask rather than treating the result as
   complete. Closed or merged dependent PRs do not prevent standalone cleanup.
6. Select exactly one path:
   - **Standalone:** the merged PR's `baseRefName` is the trunk, there are no
     active dependent PRs, no GitHub stack, and no local registration
     containing the merged PR branch.
   - **Local-only registration:** no GitHub stack, but a local registration
     contains the branch. Inspect that exact registration; do not select an
     unrelated active stack.
   - **GitHub stack:** a GitHub stack contains the PR. Continue with **Establish
     and inspect a GitHub stack**.

For a PR with a known GitHub stack, its recorded stack composition is
authoritative even when the merged layer has a non-trunk base; do not apply the
standalone checks to it. For a local-only registration, inspect that exact
registration and its PR bases. If there is an active dependent PR without
either a GitHub stack or a local registration, or a PR with no known stack has
a non-trunk base, stop before deleting branches and ask how to handle the
chain. If the local registration cannot be selected unambiguously or its
membership cannot be verified, stop and ask. Do not reconstruct a missing
branch without the user's approval.

## Standalone PR cleanup

The maintenance request includes cleanup for a standalone merged PR; do not
ask whether to prune its local branch. This path does not require `gh stack
checkout`, `gh stack sync`, or stack-number discovery.

1. **Protect local commits.** For each PR branch slated for deletion, compare
   its local tip with the confirmed `headRefOid`. Exact equality proves that
   its tip is represented by the merged PR. If they differ, fetch that PR's
   head ref `refs/pull/<number>/head` from `origin`. Verify
   `git rev-parse FETCH_HEAD` equals the confirmed `headRefOid`, then verify
   `git merge-base --is-ancestor <local-tip> <confirmed-headRefOid>` succeeds.
   If the PR ref no longer resolves to the confirmed SHA, or that SHA is not
   available locally, stop; do not substitute a newer fetched tip for the
   recorded `headRefOid`. Check the ancestry command's exit status: `0` proves
   ancestry, `1` means it is not an ancestor, and other failures mean the
   check itself failed. If the fetch or proof fails, preserve the branch and
   ask how to handle it. Never interpret a missing local or remote branch as
   permission to recreate it.
2. **Update the trunk.** Run `git fetch --prune origin`, switch to the recorded
   trunk (`main` in this repository), and run
   `git merge --ff-only origin/<trunk>`. This updates only the local trunk.
   Stop if it cannot fast-forward; do not reset, rebase, or force-update it.
3. **Remove the verified local topic branch or branches.** If a branch exists
   and its proof in step 1 passed, delete only that branch. Squash/rebase
   merges commonly leave the topic tip outside trunk history, so
   `git branch -d` may refuse even though the merged PR head proves the commits
   are represented. Use targeted `git branch -D <branch>` only after the
   ancestry proof; do not try `-d` as a probe. If a branch is already absent,
   leave it absent.
4. **Verify.** `git fetch --prune origin` removes stale local remote-tracking
   references, including the PR branch ref if GitHub has already deleted that
   remote branch; it does not delete remote branches. Confirm the worktree is
   clean, the checkout is on `<trunk>`, `git rev-list --left-right --count
<trunk>...origin/<trunk>` prints `0 0`, and every local topic branch in the
   verified cleanup set is absent.

Do not delete a remote branch as routine cleanup. Do not delete local branches
for other PRs, even if their PRs appear closed or merged.

## Clean up a local-only registration

Use this path when classification finds a local registration containing the
merged PR branch. Inspect its complete PR states before choosing partial-stack
synchronization or final cleanup. A queued branch is not merged.

First inspect the exact registration's complete PR states. If any branch is
open or queued, do not remove the registration or run final cleanup. Treat it
as a partially merged stack and use **Synchronize remaining layers**. Select
the first unmerged layer with `git switch <branch>` only when that local branch
belongs unambiguously to the verified registration. Then run
`gh stack view --json` and confirm the active registration exactly matches the
verified chain and PR bases. Do not use `gh stack checkout` if it could resolve
to a different GitHub stack. Continue at step 2 of **Synchronize remaining
layers**, skipping its checkout step. If branch selection or active
registration remains ambiguous, stop and ask.

For an all-merged local-only registration:

1. Select the registration through a surviving branch that belongs only to
   that registration. Run `gh stack view --json` and verify its complete
   composition and PR merge states. If its selecting branch is missing, or the
   branch is shared with another registration and selection is ambiguous, stop
   and ask before reconstructing or changing anything.
2. Protect every local branch slated for deletion using the standalone
   branch-tip check. If any branch has unrepresented commits, stop and ask how
   to preserve it.
3. Remove only the local registration with `gh stack unstack --local` while
   its exact stack is active. Never use bare `gh stack unstack`, which also
   changes GitHub. Do not pass a stack number unless it is confirmed and the
   CLI supports that local-only invocation.
4. Complete the verified branch cleanup and trunk fast-forward from
   **Standalone PR cleanup**, applying the tip check to every merged branch
   in this registration. Do not recreate a missing selecting branch without
   explicit approval. If the registration cannot be removed without
   reconstructing one, stop before cleanup and ask the user.

## Establish and inspect a GitHub stack

1. Run `gh stack checkout <merged-pr-url>` to select the intended GitHub stack.
   If a different local stack blocks checkout, inspect that exact registration.
   Switch to the saved merged head branch and run `gh stack view --json` to
   inspect that specific registration. If every PR in the conflicting local
   registration is merged and its identity is unambiguous, remove it with
   `gh stack unstack --local` while that branch is active, then retry
   `gh stack checkout <merged-pr-url>`. If any PR is unmerged, the branch
   cannot be selected, or the compositions disagree, stop and ask rather than
   discarding local metadata.
2. Run `gh stack view --json`. Read branches in stack order and retain the
   trunk, each branch name and head SHA, merge/queue state, and PR number.
   Never infer stack order from PR numbers.
3. If any branch has `isMerged: false` or is queued, continue with
   **Synchronize remaining layers**. If every branch is merged, continue with
   **Clean up a fully merged stack**.

## Synchronize remaining layers

1. Check out the first branch with `isMerged: false` using
   `gh stack checkout <branch>`. This handles several lower layers merged
   before recovery.
2. Run `gh stack sync`. It fetches and fast-forwards the trunk, skips merged
   layers, rebases the first unmerged layer onto the trunk, rebases later
   layers onto their predecessors, pushes the rewritten branches, and updates
   their PR bases.
3. If `gh stack sync` exits 3 on a textual conflict, it restores branches to
   their pre-sync state and leaves no conflicted files to continue. Run
   `gh stack rebase` to recreate a resumable rebase. Inspect the status and
   relevant diffs; preserve the new upstream version—trunk for the first
   unmerged branch, then the rebased predecessor for each later branch—and
   retain only the current layer's own delta. Stage resolved paths and run
   `gh stack rebase --continue`, repeating for later conflicts. Use
   `gh stack rebase --abort` to restore the entire stack if resolution must be
   abandoned. After the rebase finishes, rerun `gh stack sync`.
4. Verify the worktree is clean; every remaining branch has
   `git rev-list --left-right --count <branch>...origin/<branch>` equal to
   `0 0`; and the first unmerged PR targets the trunk while each later PR
   targets its immediate predecessor.
5. If the recorded starting branch was outside the maintained stack and still
   exists, restore it. Do not rebase or otherwise modify it.

Do not amend commits or force-push branches manually as a substitute for
`gh stack sync`. Do not rebase auxiliary branches that are not part of the
GitHub stack.

## Clean up a fully merged stack

Cleanup is part of the maintenance request once every layer has merged. Do
not run this path while a layer remains open, queued, or otherwise unmerged.

1. **Protect local-only commits.** For every branch slated for pruning, confirm
   its PR is `MERGED` and verify the local tip equals the PR's `headRefOid` or
   is an ancestor of the verified PR head, as described in **Standalone PR
   cleanup**. Stop if any branch contains unrepresented commits.
2. **Prune the completed stack.** Record the GitHub stack number when one
   exists; never infer it from a PR number. If a numbered stack's number cannot
   be confirmed, stop before pruning. Run `gh stack sync --prune` from the
   established completed stack. It fast-forwards the trunk, moves the
   checkout to the trunk when every layer is merged, and deletes local
   branches for merged PRs. It remains safe when GitHub has already deleted
   the remote branches: it uses merged PR state, not continued existence of
   those refs, and does not delete remote branches.
3. **Prune stale remote-tracking refs.** Run `git fetch --prune origin`. This
   removes local `origin/...` references only for branches already deleted on
   GitHub.
4. **Remove the local registration.** After `sync --prune`, remove only this
   completed numbered stack's local registration with
   `gh stack unstack <stack-number> --local`, using the previously confirmed
   number. Never run bare `gh stack unstack` for local cleanup. If the number
   cannot be confirmed or the command cannot identify the exact local
   registration, stop and ask; do not remove another registration.
5. **Verify.** Confirm the worktree is clean on the updated trunk, the trunk
   matches `origin/<trunk>` (`0 0`), completed local branches are absent, and
   every recorded PR remains `MERGED`.

For standalone, local-only registration, and fully merged-stack cleanup,
finish on the updated trunk even if maintenance began on an unrelated branch.
Leave unrelated local branches and their tips unchanged; do not restore the
starting branch after final cleanup.
