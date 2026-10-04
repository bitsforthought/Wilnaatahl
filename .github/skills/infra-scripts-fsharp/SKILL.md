---
name: infra-scripts-fsharp
description: >-
  Conventions for Wilnaatahl build/CI/codegen infrastructure, including the
  package-command facade and `.fsx` scripts under `scripts/` — behavioural
  self-tests, declarative FAKE graphs, portable process invocation, and semantic
  F#. Use when changing any of those surfaces.
---

# Infrastructure scripts

## Language and structure

- **Use F# scripts through `fsi`.** Implement build/CI/codegen scripts as `.fsx`
  files invoked through `dotnet fsi`, not as `.mjs`/`.sh`/`.ps1`. F# is the
  project's lingua franca for logic, and `fsi` ships with the required .NET SDK.
- **Load and apply `fsharp-style` for `.fs` and `.fsx` changes.** Infrastructure
  F# is not exempt from semantic design: separate pure logic from the I/O shell,
  use records or discriminated unions instead of multi-purpose tuples or
  combinations of boolean flags, and name values by their role. Names must
  describe actual scope and behaviour.
- **Use PascalCase file names.** F# source files and scripts use PascalCase (for
  example, `CheckCoverage.fsx`), not kebab-case or camelCase.
- **Structure by concern when the script grows.** Keep the top-level target graph
  easy to scan. Group parsing, process execution, test helpers, and target
  definitions into internal modules when they otherwise obscure one another;
  do not use a line-count threshold or mix a mechanical move with refactoring.
- **Use the CLR ecosystem.** Scripts may reference pinned NuGet packages with
  `#r "nuget: Package, x.y.z"`. Such dependencies are build-time-only. Make the
  build-vs-buy decision per `fsharp-style`; for example,
  `ValidateAgentDefinitions.fsx` uses YamlDotNet rather than a hand-rolled parser.

## Behavioural self-tests

- Start with a failing test for each repository-owned policy or observable effect
  before implementing it. State the concrete regression that the test prevents.
- Do not calculate the expected value by walking the same declarations, target
  graph, or command table as the implementation. Such a test only proves that the
  implementation agrees with itself.
- Do not baseline exact help text, package-script strings, or generated target
  lists merely to echo their declarations. Test parsing, forwarding, side
  effects, exit status, or another consumer-visible contract instead. Pin text
  only when its exact wording is itself the contract.
- Do not commit experiments that test FAKE, npm, FSI, or another dependency's
  documented behaviour. Use a disposable probe to answer the question, then keep
  a repository test only when Wilnaatahl adds policy or an adaptation that could
  regress.

## FAKE target graphs

- Keep a fixed graph declarative and visible in the target declarations. Prefer
  FAKE's dependency and ordering operators over collections of tuple edges and
  loops. Use generated edges only for a genuinely data-driven target family.
- Declare the direct prerequisite that explains each edge. Do not add redundant
  transitive edges unless the target independently requires that prerequisite
  and the edge preserves behaviour when the intermediate target changes.
- Choose the operator direction that makes the local group easiest to read;
  there is no repository-wide preference for `<==` over `==>`.
- Test repository invariants such as once-only preparation, required ordering,
  failure propagation, and stale-output rejection. Do not reproduce FAKE's
  scheduler in the expected result.

## Processes, portability, and cleanup

- Pass executable arguments as structured argument lists; do not compose shell
  command strings or chains. Centralize repeated invocation policy in one
  repository entry point instead of copying flags or platform-specific variants.
- Distinguish a command entered by a developer from a command inside an npm
  script: on Windows they may be parsed by different shells. Verify the actual
  boundary before documenting quoting or forwarding rules.
- Prefer runtime or framework facilities over assuming a named desktop or shell
  utility is installed. If platform branching is unavoidable, model the platform
  as one discriminated union rather than independent booleans.
- Verify cross-platform and cross-shell claims in the native environments that
  are available. State unavailable platforms as unverified and record a concrete
  smoke check for the future CI workflow; mocks do not prove shell behaviour.
- Treat repeated platform workarounds as a design signal. Investigate and fix the
  shared boundary before adding the same workaround to more scripts or guidance.
- Scope temporary files and directories with `use`, `try/finally`, or a helper
  that guarantees cleanup after both success and failure. Delete only paths owned
  by the active operation.
