---
name: tdd-coverage-loop
description: >-
  The build/test/coverage validation gate for Wilnaatahl — the exact command
  sequence to run before declaring any code change done, plus why each step is
  required. Use whenever validating a change or deciding whether work is
  complete.
---

# Validation gate (build, test, coverage)

Run these in order before considering any code change complete. Each catches a
class of failure the others miss.

## Commands

| Step            | Command                                       | Purpose                                                                                                                                                                               |
| --------------- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build (full)    | `npm run build`                               | Invokes Fable to compile F# core to TypeScript, then Vite. Catches Fable-emitted invalid TS that `dotnet test` cannot.                                                                |
| Tests           | `npm test`                                    | Runs .NET xUnit tests, then Koota conformance tests via Fable + vite-node.                                                                                                            |
| Koota only      | `npm run test:koota`                          | Subset of `npm test` (which already runs it after `dotnet test`) — invoke it directly only for faster Koota-only iteration.                                                           |
| Coverage gate   | `npm run coverage:check`                      | Runs all tests with coverage, generates a summary, and fails if line coverage drops below the baseline in `coverage-baseline.json`. The baseline auto-updates when coverage improves. |
| Coverage report | `npm run report --coveragefile=<path-to-xml>` | Generates an HTML report in `coveragereport/`.                                                                                                                                        |
| Format          | `npm run format`                              | Prettier for TS, Fantomas for F#.                                                                                                                                                     |

## Rules

- **Validate end-to-end before declaring done.** `dotnet test` alone is **not**
  sufficient — it only exercises the .NET-targeted F# build. Always run **`npm run
build`** and **`npm test`** before considering a change complete. Fable can emit
  invalid TypeScript for code that compiles cleanly under `dotnet test` (e.g.
  nested generics with concrete-plus-generic tuple element types — see
  `compareCouplesByEffectiveDate` and Fable issue fable-compiler/Fable#3586), so
  skipping the npm side lets those failures escape the change.
- **Check coverage after every change.** Run `npm run coverage:check` after making
  code changes and before committing.
- **Run the smallest targeted selection that covers the change**, then escalate to
  full-suite runs only when targeted validation shows they're needed.
- **Allow Prettier to update `.md` files.** That's part of its job; keep its
  markdown reformatting rather than reverting it as "unrelated".

## Other process rules

- **Line endings must be LF**, not CRLF (`.gitattributes` enforces `eol=lf`).
- **Wrap commit messages at a maximum of 80 columns.** `git commit -m` keeps each
  `-m` argument as one unwrapped line; use `git commit -F file` (or `\n` inside
  `-m`) to wrap properly. Include the `Co-authored-by: Copilot` trailer.
