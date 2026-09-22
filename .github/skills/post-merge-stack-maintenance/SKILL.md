---
name: post-merge-stack-maintenance
description: >-
  Repairs a gh-stack PR stack after a lower pull request is merged, including
  squash- and rebase-merge ancestry recovery, and performs optional one-time
  cleanup after the final PR merges. Use whenever a stacked PR is merged, the
  remaining stack needs rebasing, or a fully merged stack needs pruning.
---

# Maintain a PR stack after merging a layer

Squash and rebase merges both put rewritten commits on the trunk rather than
preserving the merged branch's original commit identities. The remaining stack
still descends from those original commits, so an ordinary rebase can replay
changes that are already merged or conflict with them. Use `gh stack sync`, which
recognizes merged PRs and adjusts the remaining ancestry; do not manually rebase
each branch or duplicate already-merged changes.

## Procedure

1. **Require a clean worktree.** Run `git status --short --branch`. Do not stash,
   discard, or move unrelated changes without the user's approval. Record the
   starting branch so an auxiliary branch can be restored after maintenance.
2. **Confirm the merge.** Run
   `gh pr view <merged-number> --json state,mergedAt,mergeCommit,headRefName,baseRefName,url`
   and verify the PR is merged. Keep the returned PR URL and head branch name.
3. **Establish the intended stack.** Run
   `gh stack checkout <merged-pr-url>` even if the current branch already belongs
   to a stack; otherwise a valid but unrelated active stack could be synchronized.
   If checkout reports that a different local stack already covers those
   branches, switch to the saved merged head branch and run
   `gh stack view --json` to inspect that specific local stack. Compare its
   composition with GitHub. When GitHub is authoritative, run
   `gh stack unstack --local` while that branch makes the conflicting stack
   active, then retry the PR-URL checkout. If the branch cannot be selected or it
   is not clear which composition to keep, stop and ask the user rather than
   discarding local stack metadata.
4. **Inspect the stack.** Run `gh stack view --json` and read the reported
   branches in stack order. Retain the trunk name and each branch's name, head
   SHA, merge state, and PR number for verification after cleanup. A queued branch
   is still unmerged. Do not infer stack position by adding one to a GitHub PR
   number: PR numbers are repository-wide and need not be consecutive within a
   stack.
5. **Choose the applicable path.**
   - If any branch has `isMerged: false`, continue with **Rebase the remaining
     stack**.
   - If every branch has `isMerged: true`, skip rebasing and continue with
     **Clean up a fully merged stack**.

## Rebase the remaining stack

1. **Check out the first unmerged layer.** Select the first branch with
   `isMerged: false` and switch to it with `gh stack checkout <branch>`. This also
   handles several lower layers being merged before recovery.
2. **Synchronize the stack.** Run `gh stack sync`. It should:
   - fetch and fast-forward the trunk;
   - skip every merged layer;
   - rebase the first unmerged branch onto the trunk, adjusted for the merged PR;
   - rebase each later branch onto its predecessor;
   - push the rewritten remaining branches; and
   - update the remaining PR base branches on GitHub.
3. **If synchronization exits 3 on a textual conflict, recreate a resumable
   rebase.** A failed `gh stack sync` restores every branch to its pre-sync state,
   so it leaves no conflicted files to continue. Run `gh stack rebase`; that
   command stops at the conflict. Inspect `git status` and the relevant diffs,
   preserve the new upstream version—trunk for the first unmerged branch, then
   the rebased predecessor for each later branch—and retain only the current
   layer's own delta. Stage the resolved paths and run
   `gh stack rebase --continue`, repeating for later conflicts. Use
   `gh stack rebase --abort` to restore the entire stack if the resolution must be
   abandoned. After the rebase finishes, rerun `gh stack sync` to push the
   rewritten branches and update the PR bases.
4. **Verify every remaining layer.**
   - `git status --short --branch` must be clean.
   - `git rev-list --left-right --count <branch>...origin/<branch>` must print
     `0 0` for every remaining branch.
   - `gh pr view <number> --json state,baseRefName,headRefName` must show the
     first unmerged PR targeting the trunk and each later PR targeting its
     immediate predecessor.
5. **Restore an auxiliary starting branch.** If the recorded starting branch was
   outside the maintained stack and still exists, switch back to it. Do not rebase
   or otherwise modify it unless the user asks.

Do not amend commits or force-push branches manually as a substitute for
`gh stack sync`. The command owns the coordinated history rewrite and push for
the linked stack. Do not rebase auxiliary branches that are not part of the
GitHub stack; leave those for their owner unless the user explicitly asks.

### Example

For this illustrative stack, where the branch suffixes happen to match the PR
numbers:

```text
main <- stack/07 (merged) <- stack/08 <- stack/09 <- stack/10
```

check out `stack/08` and run `gh stack sync`. The expected result is:

```text
main <- stack/08 <- stack/09 <- stack/10
```

with PR #8 targeting `main`, PR #9 targeting `stack/08`, and PR #10 targeting
`stack/09`.

## Clean up a fully merged stack

No rebase or push is required once every layer is merged. The stack is already
correct on GitHub; the remaining work is optional local housekeeping and must run
only after `gh stack view --json` confirms that every branch has
`isMerged: true`. If the user did not request cleanup, restore an auxiliary
starting branch, report that no required maintenance remains, and stop without
deleting branches.

1. **Protect local-only commits.** For every branch slated for pruning, run
   `gh pr view <number> --json headRefOid,state` and verify that the PR state is
   `MERGED` and `git rev-parse <branch>` equals `headRefOid`. A mismatch means the
   local branch contains a commit that is not represented by the merged PR; stop
   and ask the user how to preserve it.
2. **Prune the completed local stack.** Run `gh stack sync --prune` from the
   established completed stack. This fast-forwards the trunk, moves the checkout
   to the trunk when every layer is merged, and deletes local branches for merged
   PRs. Do not run it while any layer remains unmerged.
3. **Prune stale remote-tracking references.** Run `git fetch --prune`. This
   removes local `origin/...` references only for branches already deleted on the
   remote; it does not delete remote branches.
4. **Restore an auxiliary starting branch.** If the recorded starting branch was
   outside the completed stack and still exists, switch back to it. Do not rebase
   or otherwise modify it unless the user asks.
5. **Verify cleanup.**
   - `git status --short --branch` must be clean.
   - `git rev-list --left-right --count <trunk>...origin/<trunk>` must print
     `0 0`.
   - `git show-ref --verify --quiet refs/heads/<branch>` must fail for every
     completed stack branch, proving each local branch is absent.
   - `gh pr view <number> --json state --jq .state` must print `MERGED` for every
     PR retained from the pre-prune stack view.

Do not manually delete remote branches as part of routine cleanup. Repository
settings may retain them deliberately; remove them only when the user explicitly
asks.
