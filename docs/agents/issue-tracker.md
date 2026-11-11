# Issue tracker: GitHub

Issues and specs live in GitHub Issues for `bitsforthought/Wilnaatahl`. Use the
`gh` CLI from this clone.

## Conventions

- Before the first issue creation of a session, verify the target with
  `gh repo view --json nameWithOwner`. If it differs from
  `bitsforthought/Wilnaatahl`, confirm with the user before writing; forks can
  default to upstream.
- Create: `gh issue create --title "..." --body "..."`. For multiline bodies,
  use `--body-file` with a UTF-8 file or PowerShell here-string piped to
  `--body-file -`.
- Read: `gh issue view <number> --json number,title,body,labels,comments`.
- List: `gh issue list --state open --json number,title,body,labels,comments`,
  with appropriate label and state filters.
- Comment: `gh issue comment <number> --body "..."`.
- Apply/remove labels: `gh issue edit <number> --add-label "..."` /
  `--remove-label "..."`.
- Close: post the explanation with `gh issue comment`, then `gh issue close`.
  Keep these separate so an already-closed issue still receives the explanation.

## Sub-issues and blocking

Use GitHub's issue relationships when available:

- Link a child:
  `gh api --method POST repos/bitsforthought/Wilnaatahl/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`.
- Add a blocker:
  `gh api --method POST repos/bitsforthought/Wilnaatahl/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`.
- Obtain a database ID:
  `gh api repos/bitsforthought/Wilnaatahl/issues/<number> --jq .id`.
  Relationship APIs require this numeric ID, not the issue number or node ID.
- Count open blockers:
  `gh api repos/bitsforthought/Wilnaatahl/issues/<number> --jq .issue_dependencies_summary.blocked_by`.
  A ticket is unblocked when every blocker is closed.

If sub-issues are unavailable, put `Part of #<parent>` at the top of the child
body and link the child in the parent's task list. If dependencies are
unavailable, put `Blocked by: #<number>, #<number>` at the top of the child body
and check each blocker's state.

## Pull requests as a triage surface

**PRs as a request surface: no.** Set to `yes` to include external PRs in the
triage queue.

When enabled, use `gh pr view <number>`, `gh pr view <number> --comments`, and
`gh pr diff <number>` to read the body, comments, and diff separately. Use the
`gh pr` equivalents to comment, label, and close. List external PRs with
`gh api --paginate 'repos/bitsforthought/Wilnaatahl/pulls?state=open' --jq '.[] | select(.author_association | IN("OWNER","MEMBER","COLLABORATOR") | not)'`.

Issues and PRs share a number space; when the type is unknown, try
`gh pr view <number>` and fall back to `gh issue view <number>`.

## Skill operations

- "Publish to the issue tracker": create a GitHub issue.
- "Fetch the relevant ticket": read the issue using the command above.
- `/wayfinder`: the map is an issue labelled `wayfinder:map`; tickets are child
  issues labelled `wayfinder:<type>` (`research`, `prototype`, `grilling`, or
  `task`). Create missing wayfinder labels when this workflow is used.
- Frontier: read the map's sub-issues or task list; choose the first open child
  in map order with no open blockers and no assignee.
- Claim: `gh issue edit <number> --add-assignee @me`, before other writes for
  the ticket.
- Resolve: comment with the answer, close the ticket, and append a context
  pointer (gist plus link) to the map's Decisions-so-far.
