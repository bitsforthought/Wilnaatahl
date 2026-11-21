# Committed review process

The coordinator owns this process; each `adversarial-reviewer` is one read-only
panelist and does not launch another panel. The rubric owns substantive criteria.

## Candidate and packet

Pin the intended review base and candidate commit SHAs. Use the actual merge-base
for a stacked layer's diff and record it alongside its parent tip; never infer
approval from a commit's existence. Require the candidate tip to contain every
in-scope change, including new files. Pending in-scope edits mean the review is
not ready, not that those edits can be silently omitted.

Save a machine-generated diff, status and source manifest, immutable candidate
files from the pinned Git tree, acceptance criteria, applicable standards,
validation results/log paths, and load-bearing decisions in session storage.
Capture the relevant base versions and additional source needed to judge the
change. Materialize added files too. Pass paths and a short scope, not pasted
diffs or transcripts. Reviewers must be able to inspect the whole scope without
shell/Git access.

Use the pinned tree rather than mutable working copies. Keep the candidate
unchanged during a round. Changed content or review base needs matching validation
and review evidence before acceptance. After review, use the lifecycle's
verification for a same-tree squash. An unreadable packet or uninspected change
means **incomplete**, not clean; supply missing material to that panelist.

## Panel and outcomes

The coordinator chooses the panel models automatically; do not ask the user to
choose them. Use three supported models from different families: one Anthropic,
one OpenAI, and one Google. Honour explicit user model preferences while keeping
that diversity. If a model is unavailable or cannot return a complete review,
choose another supported model from the same family.
Run each as one `adversarial-reviewer`.
Wait for every panelist before consolidating or fixing findings; a lone dissenter
may have found the real defect. A failed/incomplete panelist is not a clean vote:
obtain the missing inspection evidence before accepting the round.

The first panel inspects the full candidate against the rubric and acceptance
criteria. Keep a session-local ledger of reviewer/model, finding classification,
accepted/rejected/deferred decision, evidence, and the change resolving an accepted
finding. Agreement is not itself evidence; adjudicate genuine findings rather
than voting them away.

An implementer addresses accepted findings and returns a candidate to the
coordinator. Follow the [implementation lifecycle](../../../docs/agents/implementation.md)
for validation and follow-up commits, then solicit a fresh delta panel.
Include the previous reviewed SHA, generated resolution diff, changed files,
claimed fixes, and every unresolved deferred finding with its supporting files.
Review only resolutions and regressions caused by the delta; do not re-audit
unchanged code or re-raise a rejected finding without new contrary evidence.

Stop early when a round is clean and no genuine finding remains. Permit at most
five rounds total. Resolve all deferrals as accepted or rejected before the final
round ends. A genuine finding unresolved after round five blocks completion:
raise it to the user for human review rather than begin round six or declare done.

Record the accepted base and reviewed commit SHAs, validation evidence, complete
panel results, ledger path and round count. The
[implementation lifecycle](../../../docs/agents/implementation.md#squash-before-pushing)
then defines squashing unpublished checkpoints and verifying the publication
commit against that reviewed content. Record both commit SHAs and the verification;
a changed SHA caused only by that verified squash does not require another panel.
This evidence, not a branch name or Git commit alone, authorizes completion.
Keep review artifacts outside the repository unless the user requests otherwise.
