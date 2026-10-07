# Contributing to Wilnaatahl

Thank you for helping. This guide explains how to propose a change and what has
to pass before it can be merged.

## Proposing a change

1. Fork the repository and create a branch in your fork.
2. Set up the development environment as described in the
   [README](README.md#development-instructions).
3. Make your change. Keep each pull request focused on one concern.
4. Run the validation gate locally (see below).
5. Open a pull request against `main`.

Contributors outside the project cannot push branches to this repository, so
pull requests come from forks.

## The validation gate

Every pull request runs the **Validate** workflow on Ubuntu and Windows. It runs
the same checks you can run locally: formatting, linting, type checking, the F#
and TypeScript test suites, and the coverage gates. The checks named
`Validate (ubuntu-latest)` and `Validate (windows-latest)` must pass before a
pull request can be merged.

To reproduce the gate locally, run:

```text
npm run init
npm run validate
```

`npm run validate` formats files in place and raises the coverage baseline in
`coverage-baseline.json` when coverage improves; commit those changes with your
work. In CI the same gate only checks: it fails on unformatted files or on any
coverage regression, and never writes either. See
[`docs/build.md`](docs/build.md) for focused targets and CI mode.

Coverage may not decrease. Add tests for new behaviour rather than lowering the
baseline.

The first time you contribute, a maintainer must approve the workflow run
before it starts. This is a GitHub safeguard for pull requests from outside
contributors, not a judgment of your change.

## Review

Every pull request needs an approving review from a code owner (see
[`.github/CODEOWNERS`](.github/CODEOWNERS)). Pushing new commits after approval
dismisses the approval, so the latest changes are always reviewed. Pull requests
are merged by squash or rebase; `main` keeps a linear history.

## Conventions

- Source files use LF line endings and spaces for indentation. Fantomas formats
  F# and Prettier formats everything else; `npm run format` applies both.
- Write documentation and comments in Canadian English (for example,
  "behaviour" and "colour").
- Use the correct Gitxsan terms in code and prose. See the terminology section
  in [`AGENTS.md`](AGENTS.md#gitxsan-terminology); for example, the plural of
  _Wilp_ is _Huwilp_.
- Domain logic belongs in F#, under `src/Wilnaatahl.Core/`. TypeScript and React
  stay a thin presentation layer. [`AGENTS.md`](AGENTS.md) describes the
  architecture in detail.

## Licence

Wilnaatahl is licensed under the AGPL-3.0 with an additional restriction against
commercial use; see [`LICENSE`](LICENSE). By submitting a contribution, you
agree that it is licensed under the same terms.
