// Validates the YAML frontmatter of custom-agent and skill definitions against
// the officially documented schemas, so malformed/undocumented definitions are
// caught at build time (VS Code's own diagnostics only run on open files and use
// a different schema than the Copilot CLI this project targets).
//
// Source of truth (no machine-readable schema is published anywhere):
//   - Agents: https://docs.github.com/en/copilot/reference/custom-agents-configuration
//             plus the keys VS Code's agent files accept (name, description, target,
//             tools, model, disable-model-invocation, user-invocable, github,
//             handoffs, hooks, agents, argument-hint, mcp-servers, metadata).
//   - Skills: https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills
//             SKILL.md frontmatter is name, description, license, allowed-tools.
// Last reconciled with the docs: 2026-06.
//
// The frontmatter is parsed with YamlDotNet (a standard, well-maintained .NET YAML
// library) rather than a hand-rolled parser, so YAML subtleties — duplicate keys,
// comments, quoting styles, block scalars, nested mappings, flow vs block lists —
// are handled correctly instead of by fragile bespoke string logic. YamlDotNet is
// a build-time-only dependency (this script runs under `dotnet fsi`); it is never
// part of the Fable browser bundle.
//
// Posture: error on confident/stable problems (missing required fields, malformed
// YAML, the retired `infer` key, and type errors); warn (without failing the
// build) on any unknown key, so additive format evolution never breaks the build
// while drift stays visible. An unknown key that is one edit away from a
// documented key gets a sharper "looks like a typo" warning, but is still only a
// warning — a genuinely new upstream key one edit from an existing one must not
// fail the build. Keys that exist only in the CLI's internal schema (e.g. `skills`)
// are intentionally not in the allow-list and surface as warnings.

#r "nuget: YamlDotNet, 16.3.0"

open System.IO
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

type FieldValue =
    /// A scalar value and whether it was a *plain* (unquoted, non-block) scalar.
    /// Only a plain `true`/`false` is a boolean; a quoted or block-scalar `true`
    /// is the string "true".
    | Scalar of value: string * isPlain: bool
    /// A sequence whose every element is a scalar.
    | Items of string list
    /// A sequence containing at least one non-scalar element.
    | NonStringList
    /// A nested mapping (sub-object).
    | Mapping

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

type Finding = { Severity: Severity; Message: string }

/// Converts a YAML node into the shape this validator reasons about: scalars
/// (with their quoted-ness), all-scalar sequences, sequences with a non-scalar
/// element, and nested mappings. Plain null scalars become the empty string so
/// that `description:` with no value reads as missing.
let private toFieldValue (node: YamlNode) : FieldValue =
    match node with
    | :? YamlScalarNode as scalar ->
        let isPlain = scalar.Style = ScalarStyle.Plain

        let raw = if isNull scalar.Value then "" else scalar.Value

        let value =
            if isPlain && (raw = "~" || raw = "null" || raw = "Null" || raw = "NULL") then
                ""
            else
                raw

        Scalar(value, isPlain)
    | :? YamlSequenceNode as sequence ->
        let scalars =
            sequence.Children
            |> Seq.choose (fun child ->
                match child with
                | :? YamlScalarNode as item -> Some(if isNull item.Value then "" else item.Value)
                | _ -> None)
            |> Seq.toList

        if scalars.Length = sequence.Children.Count then
            Items scalars
        else
            NonStringList
    | _ -> Mapping

/// Parses a YAML mapping block into ordered key/value pairs. Duplicate keys,
/// non-scalar keys, multiple documents, and other malformed YAML surface as a
/// parse error rather than being silently mis-parsed or crashing the run.
let private parseMapping (block: string) : Result<(string * FieldValue) list, string> =
    if block.Trim() = "" then
        Ok []
    else
        try
            use reader = new StringReader(block)
            let stream = YamlStream()
            stream.Load(reader)

            if stream.Documents.Count = 0 then
                Ok []
            elif stream.Documents.Count > 1 then
                Error "frontmatter contains multiple YAML documents"
            else
                match stream.Documents[0].RootNode with
                | :? YamlMappingNode as mapping ->
                    let validKeyed, invalidKeyed =
                        mapping.Children
                        |> Seq.toList
                        |> List.partition (fun pair ->
                            match pair.Key with
                            | :? YamlScalarNode as key -> not (isNull key.Value) && key.Value.Trim() <> ""
                            | _ -> false)

                    if not (List.isEmpty invalidKeyed) then
                        Error "frontmatter keys must be non-empty scalar strings"
                    else
                        validKeyed
                        |> List.map (fun pair -> (pair.Key :?> YamlScalarNode).Value, toFieldValue pair.Value)
                        |> Ok
                | _ -> Error "frontmatter is not a YAML mapping"
        with :? YamlException as ex ->
            let firstLine = ex.Message.Replace("\r\n", "\n").Split('\n')[0]
            Error(sprintf "invalid YAML frontmatter: %s" firstLine)

/// Extracts the leading `---` … `---` frontmatter block and parses it. The fences
/// must sit at column 0 (matched on `TrimEnd`), so an indented `---` inside a
/// block scalar does not terminate the block.
let parseFrontmatter (text: string) : Result<(string * FieldValue) list, string> =
    let lines = text.Replace("\r\n", "\n").Split('\n')

    if lines[0].TrimEnd() <> "---" then
        Error "missing YAML frontmatter (file must start with '---')"
    else
        match lines |> Array.skip 1 |> Array.tryFindIndex (fun l -> l.TrimEnd() = "---") with
        | None -> Error "unterminated YAML frontmatter (missing closing '---')"
        | Some offset -> lines[1..offset] |> String.concat "\n" |> parseMapping


// --- Documented allow-lists (see header) ---

let agentKnownKeys =
    set
        [ "agents"
          "argument-hint"
          "description"
          "disable-model-invocation"
          "github"
          "handoffs"
          "hooks"
          "model"
          "name"
          "target"
          "tools"
          "user-invocable"
          "mcp-servers"
          "metadata" ]

let skillKnownKeys = set [ "name"; "description"; "license"; "allowed-tools" ]

let booleanKeys = set [ "disable-model-invocation"; "user-invocable" ]

let listOrStringKeys = set [ "tools"; "allowed-tools"; "handoffs" ]

let nestedMappingKeys = set [ "github"; "metadata"; "mcp-servers" ]

/// Keys that were once valid but have been removed from the schema, mapped to
/// their per-key remediation advice. A `Map` (not a bare set with a fixed
/// message) keeps a future retirement a genuine one-line addition with correct
/// guidance.
let retiredKeys =
    Map [ ("infer", "use 'disable-model-invocation' and 'user-invocable' instead") ]

// --- Pure validation rules ---

let private findField key fields =
    fields |> List.tryPick (fun (k, v) -> if k = key then Some v else None)

/// Matches a scalar whose value is non-blank, yielding the trimmed text.
/// Centralizes the "present and not just whitespace" check several rules share.
let private (|NonEmptyScalar|_|) value =
    match value with
    | Scalar(s, _) when s.Trim() <> "" -> Some(s.Trim())
    | _ -> None

/// True when `candidate` is exactly one insertion, deletion, or substitution away
/// from `target` — used to treat an unknown key as a likely typo of a documented
/// one rather than a genuinely new key.
let private isOneEdit (candidate: string) (target: string) =
    let lengthGap = abs (candidate.Length - target.Length)

    if lengthGap > 1 then
        false
    elif candidate.Length = target.Length then
        (Seq.zip candidate target |> Seq.filter (fun (a, b) -> a <> b) |> Seq.length) = 1
    else
        let shorter, longer =
            if candidate.Length < target.Length then
                candidate, target
            else
                target, candidate

        [ 0 .. longer.Length - 1 ]
        |> List.exists (fun i -> longer.Remove(i, 1) = shorter)

let descriptionFindings fields =
    match findField "description" fields with
    | Some(NonEmptyScalar _) -> []
    | Some _ ->
        [ { Severity = Severity.Error
            Message = "'description' must be a non-empty string" } ]
    | None ->
        [ { Severity = Severity.Error
            Message = "missing required 'description'" } ]

let retiredFindings fields =
    fields
    |> List.choose (fun (k, _) ->
        retiredKeys
        |> Map.tryFind k
        |> Option.map (fun remediation ->
            { Severity = Severity.Error
              Message = sprintf "'%s' is retired; %s" k remediation }))

let unknownKeyFindings (known: Set<string>) fields =
    fields
    |> List.map fst
    |> List.filter (fun k -> not (retiredKeys.ContainsKey k) && not (known.Contains k))
    |> List.map (fun k ->
        if known |> Set.exists (isOneEdit k) then
            { Severity = Severity.Warning
              Message = sprintf "undocumented key '%s' looks like a typo of a documented key" k }
        else
            { Severity = Severity.Warning
              Message = sprintf "undocumented key '%s' (not in the official schema)" k })

let booleanFindings fields =
    fields
    |> List.filter (fun (k, _) -> booleanKeys.Contains k)
    |> List.choose (fun (k, v) ->
        match v with
        | Scalar(s, true) when (let t = s.Trim() in t = "true" || t = "false") -> None
        | _ ->
            Some
                { Severity = Severity.Error
                  Message = sprintf "'%s' must be a boolean (unquoted true or false)" k })

let listTypeFindings fields =
    fields
    |> List.filter (fun (k, _) -> listOrStringKeys.Contains k)
    |> List.choose (fun (k, v) ->
        match v with
        | Items _ -> None
        | NonEmptyScalar _ -> None
        | _ ->
            Some
                { Severity = Severity.Error
                  Message = sprintf "'%s' must be a string or list of strings" k })

let nestedMappingFindings fields =
    fields
    |> List.filter (fun (k, _) -> nestedMappingKeys.Contains k)
    |> List.choose (fun (k, v) ->
        match v with
        | Mapping -> None
        | _ ->
            Some
                { Severity = Severity.Error
                  Message = sprintf "'%s' must be a mapping" k })

let validateAgent fields =
    descriptionFindings fields
    @ retiredFindings fields
    @ unknownKeyFindings agentKnownKeys fields
    @ booleanFindings fields
    @ listTypeFindings fields
    @ nestedMappingFindings fields

let skillNameFindings (directory: string) fields =
    match findField "name" fields with
    | Some(NonEmptyScalar name) ->
        if name <> directory then
            [ { Severity = Severity.Warning
                Message = sprintf "skill 'name' (%s) does not match its directory (%s)" name directory } ]
        else
            []
    | Some _ ->
        [ { Severity = Severity.Error
            Message = "'name' must be a non-empty string" } ]
    | None ->
        [ { Severity = Severity.Error
            Message = "missing required 'name'" } ]

let validateSkill directory fields =
    descriptionFindings fields
    @ skillNameFindings directory fields
    @ retiredFindings fields
    @ unknownKeyFindings skillKnownKeys fields
    @ booleanFindings fields
    @ listTypeFindings fields

/// Runs a validator over raw file text, collapsing a frontmatter parse failure
/// into a single error finding (mirroring the file-scanning pipeline).
let validateText validate (text: string) : Finding list =
    match parseFrontmatter text with
    | Error msg ->
        [ { Severity = Severity.Error
            Message = "frontmatter parse error: " + msg } ]
    | Ok fields -> validate fields

let countBySeverity findings =
    let errors =
        findings |> List.filter (fun f -> f.Severity = Severity.Error) |> List.length

    let warnings =
        findings |> List.filter (fun f -> f.Severity = Severity.Warning) |> List.length

    errors, warnings

// --- Self-test (development-time RED/GREEN harness for the rules above) ---

let private selfTest () =
    // Each case asserts the exact error/warning counts AND that every required
    // substring appears among the messages, so a test cannot pass on a
    // right-count-but-wrong-reason finding.
    let check name (findings: Finding list) expectedErrors expectedWarnings (substrings: string list) =
        let errors, warnings = countBySeverity findings
        let allText = findings |> List.map (fun f -> f.Message) |> String.concat " | "
        let countsOk = errors = expectedErrors && warnings = expectedWarnings
        let substringsOk = substrings |> List.forall allText.Contains
        let ok = countsOk && substringsOk

        if ok then
            printfn "  PASS: %s" name
        else
            printfn
                "  FAIL: %s — expected %dE/%dW %A, got %dE/%dW (%A)"
                name
                expectedErrors
                expectedWarnings
                substrings
                errors
                warnings
                findings

        ok

    let agent text = validateText validateAgent text

    let skill directory text =
        validateText (validateSkill directory) text

    let validAgent =
        """---
name: demo
description: >-
    Does a thing.
user-invocable: true
---
body
"""

    let inferAgent =
        """---
name: demo
description: A thing.
infer: true
---
"""

    let skillsAgent =
        """---
name: demo
description: A thing.
skills:
    - one
    - two
---
"""

    let badBoolAgent =
        """---
name: demo
description: A thing.
user-invocable: maybe
---
"""

    let quotedBoolAgent =
        """---
name: demo
description: A thing.
user-invocable: "true"
---
"""

    let commentBoolAgent =
        """---
name: demo
description: A thing.
user-invocable: true # enabled
---
"""

    let duplicateKeyAgent =
        """---
name: demo
description: valid
description: ""
---
"""

    let nestedScalarAgent =
        """---
name: demo
description: A thing.
github: true
---
"""

    let nestedMappingAgent =
        """---
name: demo
description: A thing.
github:
  toolsets:
    - repo
---
"""

    let nonScalarListAgent =
        """---
name: demo
description: A thing.
tools: [read, { bad: true }]
---
"""

    let emptyDescAgent =
        """---
name: demo
description:
---
"""

    // Explicit CRLF line endings are the property under test, so the escapes here
    // are clearer than a derived string.
    let crlfAgent =
        "---\r\nname: demo\r\ndescription: A thing.\r\nuser-invocable: true\r\n---\r\n"

    let typoKeyAgent =
        """---
name: demo
description: A thing.
tool: ["read"]
---
"""

    let blockBoolAgent =
        """---
name: demo
description: A thing.
user-invocable: |-
  true
---
"""

    let nonScalarKeyAgent =
        """---
name: demo
description: A thing.
? [a, b]
: c
---
"""

    let emptyKeyAgent =
        """---
name: demo
description: A thing.
: orphan
---
"""

    let listRootAgent =
        """---
- a
- b
---
"""

    let unterminatedAgent =
        """---
name: demo
description: A thing.
"""

    let multiDocAgent =
        """---
name: demo
description: A thing.
...
other: y
---
"""

    let validSkill =
        """---
name: demo-skill
description: A skill.
---
"""

    let noDescSkill =
        """---
name: demo-skill
---
"""

    let userInvocableSkill =
        """---
name: demo-skill
description: A skill.
user-invocable: true
---
"""

    let mismatchSkill =
        """---
name: other
description: A skill.
---
"""

    let malformed =
        """name: demo
description: A thing.
"""

    let flowToolsAgent =
        """---
name: demo
description: A thing.
tools: ["read", "search"]
---
"""

    printfn "Running self-test..."

    let results =
        [ check "valid agent has no findings" (agent validAgent) 0 0 []
          check "retired infer is an error" (agent inferAgent) 1 0 [ "retired" ]
          check "agent skills: is a warning" (agent skillsAgent) 0 1 [ "skills" ]
          check "non-boolean user-invocable is an error" (agent badBoolAgent) 1 0 [ "must be a boolean" ]
          check "quoted boolean is an error" (agent quotedBoolAgent) 1 0 [ "must be a boolean" ]
          check "block-scalar boolean is an error" (agent blockBoolAgent) 1 0 [ "must be a boolean" ]
          check "inline comment on a boolean is stripped" (agent commentBoolAgent) 0 0 []
          check "duplicate keys are an error" (agent duplicateKeyAgent) 1 0 [ "invalid YAML frontmatter" ]
          check "nested-mapping key given a scalar is an error" (agent nestedScalarAgent) 1 0 [ "must be a mapping" ]
          check "valid nested mapping is accepted" (agent nestedMappingAgent) 0 0 []
          check "list with a non-scalar item is an error" (agent nonScalarListAgent) 1 0 [ "string or list" ]
          check "empty description is an error" (agent emptyDescAgent) 1 0 [ "non-empty" ]
          check "CRLF frontmatter is accepted" (agent crlfAgent) 0 0 []
          check "near-miss typo of a known key is a warning" (agent typoKeyAgent) 0 1 [ "typo" ]
          check "non-scalar key is a parse error" (agent nonScalarKeyAgent) 1 0 [ "scalar" ]
          check "empty key is a parse error" (agent emptyKeyAgent) 1 0 [ "scalar" ]
          check "non-mapping root is an error" (agent listRootAgent) 1 0 [ "not a YAML mapping" ]
          check "unterminated frontmatter is an error" (agent unterminatedAgent) 1 0 [ "unterminated" ]
          check "multiple documents are an error" (agent multiDocAgent) 1 0 [ "multiple" ]
          check "inline flow list tools is accepted" (agent flowToolsAgent) 0 0 []
          check "valid skill has no findings" (skill "demo-skill" validSkill) 0 0 []
          check "skill missing description is an error" (skill "demo-skill" noDescSkill) 1 0 [ "description" ]
          check "skill user-invocable is a warning" (skill "demo-skill" userInvocableSkill) 0 1 [ "user-invocable" ]
          check "skill name != directory is a warning" (skill "demo-skill" mismatchSkill) 0 1 [ "does not match" ]
          check "malformed frontmatter is an error" (agent malformed) 1 0 [ "frontmatter" ] ]

    let passed = results |> List.filter id |> List.length
    let failed = results.Length - passed
    printfn "Self-test: %d passed, %d failed." passed failed
    if failed > 0 then exit 1 else exit 0


// --- Top-level I/O and control flow ---

let repoRoot = Path.GetDirectoryName(Path.GetFullPath(__SOURCE_DIRECTORY__))
let agentsDir = Path.Combine(repoRoot, ".github", "agents")
let skillsDir = Path.Combine(repoRoot, ".github", "skills")

let scriptArgs = fsi.CommandLineArgs |> Array.skip 1

if scriptArgs |> Array.contains "--self-test" then
    selfTest ()

let agentFiles =
    if Directory.Exists agentsDir then
        Directory.GetFiles(agentsDir, "*.md") |> Array.sort
    else
        [||]

let skillFiles =
    if Directory.Exists skillsDir then
        Directory.GetFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories)
        |> Array.sort
    else
        [||]

let relative (path: string) = Path.GetRelativePath(repoRoot, path)

let agentResults =
    agentFiles
    |> Array.map (fun file -> relative file, validateText validateAgent (File.ReadAllText file))

let skillResults =
    skillFiles
    |> Array.map (fun file ->
        let directory = Path.GetFileName(Path.GetDirectoryName file)
        relative file, validateText (validateSkill directory) (File.ReadAllText file))

let allResults = Array.append agentResults skillResults

printfn "Validating %d agent and %d skill definition(s)..." agentFiles.Length skillFiles.Length

for file, findings in allResults do
    for finding in findings do
        let label =
            match finding.Severity with
            | Severity.Error -> "ERROR"
            | Severity.Warning -> "WARN"

        printfn "  %-5s %s: %s" label file finding.Message

let allFindings = allResults |> Array.toList |> List.collect snd
let totalErrors, totalWarnings = countBySeverity allFindings

printfn "Result: %d error(s), %d warning(s)." totalErrors totalWarnings

if totalErrors > 0 then
    exit 1
