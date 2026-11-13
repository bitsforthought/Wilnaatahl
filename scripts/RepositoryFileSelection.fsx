open System
open System.IO
open System.Text.RegularExpressions

/// This is an exact directory list, not a general gitignore language: all consumers
/// must agree on the same ownership boundary without implementing glob semantics.
let parse (text: string) =
    let entries =
        text.Replace("\r\n", "\n").Split '\n'
        |> Array.toList
        |> List.filter (fun line -> not (String.IsNullOrWhiteSpace line || line.StartsWith '#'))

    let distinctEntries = entries |> Set.ofList

    // Match one whole .agents/skills/<name>/ path; names have lowercase letters,
    // digits, and single separating hyphens, with no glob or traversal syntax.
    if
        entries
        |> List.exists (fun line -> not (Regex.IsMatch(line, @"\A\.agents/skills/[a-z0-9]+(?:-[a-z0-9]+)*/\z")))
    then
        Error "Vendor skill entries must be exact .agents/skills/<lowercase-name>/ directory paths."
    elif distinctEntries |> Set.count <> entries.Length then
        Error "Vendor skill directory entries must not be duplicated."
    else
        Ok distinctEntries

let policyPath root =
    Path.Combine(root, ".vendor-skills.ignore")

let load root =
    root |> policyPath |> File.ReadAllText |> parse |> Result.defaultWith failwith

let isExcluded root excluded path =
    let relative =
        Path.GetRelativePath(root, path).Replace('\\', '/').TrimEnd('/') + "/"

    let comparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    excluded |> Set.exists (fun prefix -> relative.StartsWith(prefix, comparison))

// Build outputs, restored packages and Fable-generated code are not authored here;
// fixing them would be overwritten or would rewrite third-party sources.
// Deliberately name-based so new projects need no registration.
let private skippedDirectoryNames =
    set [ ".git"; "bin"; "obj"; "node_modules"; "fable_modules"; "generated" ]

let rec filesUnder root excluded extensions directory =
    seq {
        yield!
            Directory.EnumerateFiles directory
            |> Seq.filter (fun path -> extensions |> Set.contains (Path.GetExtension path))
            |> Seq.filter (isExcluded root excluded >> not)

        yield!
            Directory.EnumerateDirectories directory
            |> Seq.filter (fun child -> skippedDirectoryNames |> Set.contains (Path.GetFileName child) |> not)
            |> Seq.filter (isExcluded root excluded >> not)
            |> Seq.collect (filesUnder root excluded extensions)
    }

let rec private skillsUnder root excluded directory =
    seq {
        yield! Directory.EnumerateFiles(directory, "SKILL.md")

        yield!
            Directory.EnumerateDirectories directory
            |> Seq.filter (isExcluded root excluded >> not)
            |> Seq.collect (skillsUnder root excluded)
    }

let skillFiles root excluded =
    [
        Path.Combine(root, ".github", "skills")
        Path.Combine(root, ".agents", "skills")
    ]
    |> Seq.filter Directory.Exists
    |> Seq.collect (skillsUnder root excluded)
    |> Seq.sort
    |> Seq.toList

module SelfTest =
    let private assertEqual name expected actual =
        if actual <> expected then
            failwith $"Repository file selection self-test failed: {name}. Expected {expected}, got {actual}."

    let runSelfTest ownershipFixture =
        let fixtureFile relativePath (text: string) =
            let path = Path.GetFullPath(Path.Combine(ownershipFixture, relativePath))
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, text)
            path

        try
            fixtureFile ".vendor-skills.ignore" ".agents/skills/research/\n" |> ignore
            let excluded = load ownershipFixture
            let vendor = fixtureFile ".agents/skills/research/SKILL.md" "malformed upstream"
            let owned = fixtureFile ".agents/skills/research-tools/SKILL.md" "malformed owned"
            let legacy = fixtureFile ".github/skills/legacy/SKILL.md" "---\nname: legacy\n---"

            let namedLikeOutput =
                fixtureFile ".github/skills/bin/SKILL.md" "---\nname: bin\n---"

            fixtureFile ".agents/skills/research/scripts/Probe.fsx" "let vendor=1" |> ignore

            let ownedScript =
                fixtureFile ".agents/skills/research-tools/Probe.fsx" "let owned = 1"

            assertEqual "vendor skill is excluded" true (isExcluded ownershipFixture excluded vendor)
            assertEqual "similarly named owned skill is governed" false (isExcluded ownershipFixture excluded owned)

            assertEqual
                "both roots discover only owned skills"
                (set [ owned; legacy; namedLikeOutput ])
                (skillFiles ownershipFixture excluded |> Set.ofList)

            assertEqual
                "F# discovery excludes nested vendor scripts"
                [ ownedScript ]
                (filesUnder ownershipFixture excluded (set [ ".fsx" ]) ownershipFixture
                 |> Seq.toList)

            for invalid in
                [
                    ".agents/skills/*/"
                    "! .agents/skills/research/"
                    "/.agents/skills/research/"
                    ".agents/skills/../"
                    ".agents/skills/Research/"
                    ".agents/skills/research"
                    ".agents/skills/research/nested/"
                    ".agents/skills/_research/"
                    " .agents/skills/research/"
                    ".agents/skills/research/ "
                ] do
                assertEqual
                    $"invalid vendor policy is rejected: {invalid}"
                    (Error "Vendor skill entries must be exact .agents/skills/<lowercase-name>/ directory paths.")
                    (parse invalid)

            assertEqual
                "duplicate vendor directories have a distinct error"
                (Error "Vendor skill directory entries must not be duplicated.")
                (parse ".agents/skills/research/\n.agents/skills/research/")

            assertEqual
                "comments blank lines and CRLF are accepted"
                (Ok(set [ ".agents/skills/research/"; ".agents/skills/vendor/" ]))
                (parse "# Upstream\r\n\r\n  \r\n.agents/skills/research/\r\n.agents/skills/vendor/\r\n")

            assertEqual "empty policy governs everything" (Ok Set.empty) (parse "")

            let missingPolicyFails =
                try
                    load (Path.Combine(ownershipFixture, "missing")) |> ignore
                    false
                with :? DirectoryNotFoundException ->
                    true

            assertEqual "missing policy fails closed" true missingPolicyFails
        finally
            Directory.Delete(ownershipFixture, true)
