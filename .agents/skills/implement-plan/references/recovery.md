# Stack recovery

Use the [implementation lifecycle](../../../../docs/agents/implementation.md)
for completion evidence and [`gh-stack`](../../gh-stack/SKILL.md) for Git operations.

## Resume a step

**Reconcile** the session record with Git and GitHub. Resume unfinished work;
require matching completion evidence, not a state label or commit alone.

## Verify publication

**Reconcile** what reached GitHub; reuse existing PRs and finish only missing
operations. Restore absent tracking; stop on partial or conflicting configuration.
Meet the [publication checks](../SKILL.md#complete-each-step).

## Repair a lower layer

Fix and squash the owning layer, then **restack** with `gh-stack`. Require matching
validation and review for every affected layer before publishing the repaired
suffix together.

For descendants, record the rebased published tip as the squash boundary.
Preserve that history; squash only later fix checkpoints.
