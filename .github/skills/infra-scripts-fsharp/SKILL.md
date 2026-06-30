---
name: infra-scripts-fsharp
description: >-
  Conventions for build/CI/codegen infrastructure scripts in Wilnaatahl —
  written as `.fsx` run through `dotnet fsi`, with PascalCase filenames. Use
  when adding or editing a build, coverage, or Fable-patching script under
  `scripts/`.
---

# Infrastructure scripts

- **Infrastructure scripts in F# via `fsi`.** Always implement build/CI/codegen
  scripts as `.fsx` files invoked through `dotnet fsi`, not as `.mjs`/`.sh`/`.ps1`.
  F# is the project's lingua franca for logic (see `scripts/CheckCoverage.fsx`,
  `scripts/PatchFableModules.fsx`), and `fsi` ships with the .NET SDK we already
  require, so there's no extra runtime to manage. The same code-style rules apply
  (DU error types, separated pure/impure code, top-level I/O) — see the
  `fsharp-style` skill.
- **PascalCase file names.** F# source files and scripts use PascalCase (e.g.,
  `CheckCoverage.fsx`, `Model.fs`), not kebab-case or camelCase.
- **Scripts run on the CLR, so the full NuGet ecosystem is available** via
  `#r "nuget: Package, x.y.z"` in `dotnet fsi` (pin the version). Such a dependency
  is build-time-only — it never enters the Fable browser bundle — so its footprint
  and licensing risk are low. Before adding one, make the build-vs-buy call per the
  `fsharp-style` skill; e.g. `ValidateAgentDefinitions.fsx` uses **YamlDotNet**
  rather than a hand-rolled YAML parser.
