---
name: commit-messages
description: >-
  Writes informative Git commit messages for Wilnaatahl with a concise subject,
  a descriptive body explaining what changed and why, correct wrapping, and
  required trailers. Use whenever creating, revising, or reviewing a commit
  message.
---

# Commit messages

Every commit message has a concise subject followed by at least one descriptive
body paragraph. Brevity means omitting noise, not omitting context a reviewer
needs to understand the change.

## Content

- Write the subject in the imperative mood as a short summary of the commit's
  purpose. Do not end it with a period.
- After a blank line, write at least one paragraph describing both **what
  changed** and **why**. Name the behaviour, constraint, defect, or design
  decision that motivated the change.
- Include non-obvious compatibility concerns, preserved behaviour, or deliberate
  trade-offs when they matter. Leave routine implementation details to the diff.
- Do not merely list changed files, restate identifiers visible in the diff, or
  turn the body into an exhaustive change log.
- Keep each commit focused enough that one subject and a short body accurately
  describe it.

For example:

```text
Improve import error messages

Normalize arbitrary rejected values without allowing hostile string conversion
to hide the original read failure. Preserve useful Error-like messages and JSON
diagnostics while retaining a safe fallback for unprintable values.
```

`Add tests` is too vague. A long bullet-by-bullet narration of every assertion is
too detailed. The body should instead explain the behavioural guarantee the tests
add and why that guarantee matters.

## Formatting and trailers

- Wrap every line at a maximum of 80 columns.
- Use `git commit -F <file>` so the subject, paragraphs, wrapping, and trailers
  are preserved exactly; multiple `git commit -m` arguments are easy to leave
  unwrapped.
- When revising an existing commit, determine whether it has been pushed before
  changing history. For a message-only revision of an unpushed commit, use
  `git commit --amend --only -F <file>` so unrelated staged changes are not folded
  into it. Inspect the index before and after amending. If the user intends to
  amend the content too, verify the staged diff before omitting `--only`. For a
  pushed commit, follow the published-history policy in `AGENTS.md`: create a
  follow-up commit by default, and amend only with explicit approval or when the
  branch is known to be unshared.
- End with the repository-required trailers, separated from the body by a blank
  line:

```text
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
Copilot-Session: <current-session-id>
```

Inspect the complete staged diff before writing the message. Describe only what
the commit actually contains, not planned follow-up work or unstaged changes.
