# Resume and repair a stack

Use the [implementation lifecycle](../../../../docs/agents/implementation.md)
for validation, review, squashing, and evidence reuse. `gh-stack` owns command
semantics and conflict recovery.

## Resume a step

Reconcile the session record with the actual branch, diff, stack JSON, and PR.
Preserve existing work and ask only when its scope or ownership is unclear.
The harness chooses how to store or update the record; missing fields are missing
evidence, not proof that a step passed.

| Found state                                        | Next action                                                                                             |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Known assigned edits remain incomplete             | Resume those edits and focused checks                                                                   |
| Checkpoint committed, review not complete          | Obtain missing validation or review; do not publish                                                     |
| Review passed, not yet pushed                      | Verify the recorded base/content, finish the squash step, and publish                                   |
| Push or PR creation was interrupted                | Find the existing PR by branch, then finish verification rather than create a duplicate                 |
| Parent, content, or branch differs from the record | Establish why; obtain matching checks/review instead of resetting refs or relabelling the step accepted |

For a squashed commit, use the recorded same-tree and parent verification to
connect publication to the reviewed checkpoint. A changed SHA alone is expected;
missing verification is not approval.

## Verify publication

Read the PR number from `branches[].pr` in `gh stack view --json`. Confirm the
first remaining PR targets the delivery base and later PRs target their immediate
predecessor. Require OPEN/non-draft state; use `gh pr ready` if needed.

Verify each branch tracks `origin/<branch>`. Set that upstream only when both
tracking fields are absent. Stop on partial or conflicting configuration rather
than overwrite it. Repairing a missing upstream does not require another push.
Verify local and remote tips agree before recording publication.

On a failed submission, keep the accepted commit and evidence and inspect what
GitHub received. Retry only the missing operation. Never change repository
settings to work around an error.

## Repair a lower layer

Keep the fix on its owning branch. Require a clean worktree before switching;
preserve unfinished work and ask when it prevents the switch.
Record the affected branches and their tips before changing the stack.

Complete the fix against the owning layer's parent using the lifecycle, including
its verified unpublished squash. Keep it unpublished while descendants need work.
Run `gh stack rebase --upstack --remote origin`.

For every affected descendant, establish validation and review evidence against
its new parent. Use a scoped delta review and valid check reuse where appropriate;
old SHAs alone do not approve the new combination. Resolve conflicts using
`gh-stack`'s recovery instructions. Stop on a failed gate or unresolved review.

Preserve the rebased copies of already-published descendant commits as a group;
they are not new review-fix checkpoints to squash. Record their rebased tip and
the unchanged remote tip before making fixes. If review adds local fix commits,
squash only those fixes with the rebased tip as their parent. This preserves the
restack performed by `gh-stack` instead of reintroducing the old ancestry.

Once the whole affected suffix is accepted, submit with the explicit remote,
verify upstreams and PR bases/readiness, and update the records and changed bodies.
Let `gh-stack` own its required rebase/push operations; do not force-push manually.
Keep previously pushed owning-layer commits unchanged.

If repair was interrupted, reconcile the recorded suffix and finish its missing
checks before normal publication. Avoid replaying an already-completed rebase.
