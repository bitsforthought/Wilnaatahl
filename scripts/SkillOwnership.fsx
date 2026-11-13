open System
open System.IO
open System.Text.RegularExpressions

/// This is an exact directory list, not a general gitignore language: all consumers
/// must agree on the same ownership boundary without implementing glob semantics.
let parse (text: string) =
    let entries =
        text.Replace("\r\n", "\n").Split '\n'
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line || line.StartsWith '#'))

    let distinctEntries = entries |> Set.ofArray

    if
        entries
        |> Array.exists (fun line -> not (Regex.IsMatch(line, @"\A\.agents/skills/[a-z0-9]+(?:-[a-z0-9]+)*/\z")))
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
    |> Seq.toArray
