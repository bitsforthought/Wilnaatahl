open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes

module internal Maintenance =
    type ResultBuilder() =
        member _.Bind(result, binder) = Result.bind binder result
        member _.Return(value) = Ok value
        member _.ReturnFrom(result) = result
        member _.Zero() = Ok()
        member _.Delay(generator) = generator
        member _.Run(generator) = generator ()
        member _.Combine(first, second) = Result.bind (fun () -> second ()) first

    let result = ResultBuilder()

    type PullRequest = {
        Number: int
        Url: string
        State: string
        HeadBranch: string
        HeadSha: string
        BaseBranch: string
        MergeCommitSha: string option
    }

    type OpenPullRequest = { Number: int; HeadBranch: string; HeadSha: string }

    type Registration = { TrunkBranch: string; Branches: string list }

    type DependencyEvidence = {
        ActivePullRequests: OpenPullRequest list
        LocalBranches: string list
        UnverifiedStackBranches: string list
    }

    type Evidence = {
        TrunkBranch: string
        BaseBranch: string
        HeadBranch: string
        GitHubStackNumber: int option
        AllRegistrations: Registration list
        MatchingRegistrations: Registration list
        Dependencies: DependencyEvidence option
    }

    type Classification =
        | Standalone
        | LocalOnlyRegistration of Registration
        | GitHubStack of number: int
        | Stop of reason: string

    let private unregisteredDependants (registration: Registration) dependencies =
        let registeredBranches = registration.Branches |> Set.ofList

        let pullRequestBranches =
            dependencies.ActivePullRequests
            |> List.choose (fun pullRequest ->
                if Set.contains pullRequest.HeadBranch registeredBranches then
                    None
                else
                    Some pullRequest.HeadBranch)

        let localBranches =
            dependencies.LocalBranches
            |> List.filter (fun branch -> not (Set.contains branch registeredBranches))

        pullRequestBranches @ localBranches |> List.distinct

    let private registrationOverlaps (registration: Registration) (other: Registration) =
        registration.Branches
        |> List.exists (fun branch -> other.TrunkBranch = branch || List.contains branch other.Branches)

    let relatedLocalRegistrations
        (headBranch: string)
        (allRegistrations: Registration list)
        (matchingRegistrations: Registration list)
        =
        allRegistrations
        |> List.filter (fun candidate ->
            candidate.TrunkBranch = headBranch
            || (matchingRegistrations
                |> List.exists (fun selected ->
                    not (List.contains candidate matchingRegistrations)
                    && registrationOverlaps selected candidate)))

    let classify evidence =
        let matchingRegistration =
            match evidence.MatchingRegistrations with
            | [] -> Ok None
            | [ registration ] -> Ok(Some registration)
            | _ -> Error "More than one local registration contains this PR branch."

        let dependentRegistrations =
            evidence.AllRegistrations
            |> List.filter (fun registration -> registration.TrunkBranch = evidence.HeadBranch)

        match matchingRegistration with
        | Error reason -> Stop reason
        | Ok registration ->
            let overlappingRegistrations =
                match registration with
                | None -> []
                | Some localRegistration ->
                    relatedLocalRegistrations
                        evidence.HeadBranch
                        evidence.AllRegistrations
                        evidence.MatchingRegistrations
                    |> List.filter (fun other ->
                        other.TrunkBranch <> evidence.HeadBranch
                        && registrationOverlaps localRegistration other)

            let unregistered =
                match registration, evidence.Dependencies with
                | Some localRegistration, Some dependencies -> unregisteredDependants localRegistration dependencies
                | None, Some dependencies ->
                    (dependencies.ActivePullRequests |> List.map _.HeadBranch)
                    @ dependencies.LocalBranches
                | _, None -> []

            let unverifiedStackBranches =
                evidence.Dependencies
                |> Option.map _.UnverifiedStackBranches
                |> Option.defaultValue []

            match evidence.GitHubStackNumber, registration with
            | Some stackNumber, _ when stackNumber <= 0 -> Stop "GitHub returned an invalid stack number."
            | Some _, _ when Option.isNone evidence.Dependencies ->
                Stop "Dependent PR and local branch checks were not run."
            | None, Some _ when Option.isNone evidence.Dependencies ->
                Stop "Dependent PR and local branch checks were not run."
            | _, _ when not (List.isEmpty dependentRegistrations) ->
                Stop "Another local registration uses the merged PR branch as its trunk."
            | _, _ when not (List.isEmpty overlappingRegistrations) ->
                Stop "The local registration shares a branch with another registration."
            | Some _, Some _ when not (List.isEmpty unregistered) ->
                let branchNames = String.Join(", ", unregistered)
                Stop $"Dependent branches are outside the local registration: {branchNames}."
            | Some _, _ when not (List.isEmpty unverifiedStackBranches) ->
                let branchNames = String.concat ", " unverifiedStackBranches
                Stop $"Dependent branches are outside the GitHub stack: {branchNames}."
            | Some stackNumber, _ -> GitHubStack stackNumber
            | None, Some _ when not (List.isEmpty unregistered) ->
                let branchNames = String.Join(", ", unregistered)
                Stop $"Dependent branches are outside the local registration: {branchNames}."
            | None, Some localRegistration -> LocalOnlyRegistration localRegistration
            | None, None when evidence.BaseBranch <> evidence.TrunkBranch ->
                Stop "The PR has a non-trunk base but no verified stack registration."
            | None, None ->
                match evidence.Dependencies with
                | None -> Stop "Dependent PR and local branch checks were not run."
                | Some dependencies when not (List.isEmpty dependencies.ActivePullRequests) ->
                    let pullRequestNumbers =
                        dependencies.ActivePullRequests
                        |> List.map (fun pullRequest -> string pullRequest.Number)
                        |> String.concat ", "

                    Stop $"Open dependent PRs have no verified stack registration: {pullRequestNumbers}."
                | Some dependencies when not (List.isEmpty dependencies.LocalBranches) ->
                    let branchNames = String.Join(", ", dependencies.LocalBranches)
                    Stop $"Local branches depend on this PR without a verified stack registration: {branchNames}."
                | Some _ -> Standalone

    let private property (name: string) (element: JsonElement) =
        let mutable value = Unchecked.defaultof<JsonElement>

        if element.ValueKind = JsonValueKind.Object && element.TryGetProperty(name, &value) then
            Ok value
        else
            Error $"Required JSON property '{name}' is missing."

    let private stringProperty name element =
        property name element
        |> Result.bind (fun value ->
            if value.ValueKind <> JsonValueKind.String then
                Error $"JSON property '{name}' must be a string."
            else
                match value.GetString() with
                | text when not (String.IsNullOrWhiteSpace text) -> Ok text
                | _ -> Error $"JSON property '{name}' must not be empty.")

    let private integerProperty name element =
        property name element
        |> Result.bind (fun value ->
            match value.ValueKind with
            | JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, number -> Ok number
                | false, _ -> Error $"JSON property '{name}' must be a 32-bit integer."
            | _ -> Error $"JSON property '{name}' must be a number.")

    let private parseJson (parser: JsonElement -> Result<'value, string>) (text: string) =
        try
            use document = JsonDocument.Parse text
            parser document.RootElement
        with :? JsonException as error ->
            Error $"Invalid JSON: {error.Message}"

    let private sequenceResults results =
        List.foldBack
            (fun current accumulated ->
                match current, accumulated with
                | Ok value, Ok values -> Ok(value :: values)
                | Error error, _ -> Error error
                | _, Error error -> Error error)
            results
            (Ok [])

    let parsePullRequest (text: string) =
        parseJson
            (fun root ->
                result {
                    let! number = integerProperty "number" root
                    let! url = stringProperty "url" root
                    let! state = stringProperty "state" root
                    let! headBranch = stringProperty "headRefName" root
                    let! headSha = stringProperty "headRefOid" root
                    let! baseBranch = stringProperty "baseRefName" root
                    let! merge = property "mergeCommit" root

                    let! mergeCommitSha =
                        if merge.ValueKind = JsonValueKind.Null then
                            Ok None
                        else
                            stringProperty "oid" merge |> Result.map Some

                    return {
                        Number = number
                        Url = url
                        State = state
                        HeadBranch = headBranch
                        HeadSha = headSha
                        BaseBranch = baseBranch
                        MergeCommitSha = mergeCommitSha
                    }
                })
            text

    let validateMergedPullRequest (pullRequest: PullRequest) =
        if pullRequest.State = "MERGED" then
            Ok()
        else
            Error $"PR #{pullRequest.Number} is in state {pullRequest.State}, not MERGED."

    let parseRepository (text: string) =
        parseJson
            (fun root ->
                result {
                    let! nameWithOwner = stringProperty "nameWithOwner" root
                    let! defaultBranch = property "defaultBranchRef" root
                    let! trunkBranch = stringProperty "name" defaultBranch
                    return nameWithOwner, trunkBranch
                })
            text

    let parseGitHubStack (text: string) =
        parseJson
            (fun root ->
                result {
                    let! data = property "data" root
                    let! repository = property "repository" data
                    let! pullRequest = property "pullRequest" repository
                    let! stack = property "stack" pullRequest

                    if stack.ValueKind = JsonValueKind.Null then
                        return None
                    else
                        let! number = integerProperty "number" stack
                        return Some number
                })
            text

    let parseRegistrations (expectedRepository: string) (text: string) =
        parseJson
            (fun root ->
                result {
                    let! schemaVersion = integerProperty "schemaVersion" root
                    let! repository = stringProperty "repository" root

                    if schemaVersion <> 1 then
                        return! Error $"Unsupported local stack registration schema version {schemaVersion}."

                    if repository <> expectedRepository then
                        return!
                            Error $"Local stack registration belongs to '{repository}', not '{expectedRepository}'."

                    let! stacks = property "stacks" root

                    if stacks.ValueKind <> JsonValueKind.Array then
                        return! Error "Local stack registration 'stacks' must be an array."

                    let registrations =
                        stacks.EnumerateArray()
                        |> Seq.map (fun stack ->
                            result {
                                let! trunk = property "trunk" stack
                                let! trunkBranch = stringProperty "branch" trunk
                                let! branches = property "branches" stack

                                if branches.ValueKind <> JsonValueKind.Array then
                                    return! Error "Local stack registration branches must be an array."

                                let! branchNames =
                                    branches.EnumerateArray()
                                    |> Seq.map (stringProperty "branch")
                                    |> Seq.toList
                                    |> sequenceResults

                                return { TrunkBranch = trunkBranch; Branches = branchNames }
                            })
                        |> Seq.toList

                    return! sequenceResults registrations
                })
            text

    let parseOpenPullRequests (text: string) =
        parseJson
            (fun root ->
                if root.ValueKind <> JsonValueKind.Array then
                    Error "Open PR list must be a JSON array."
                else
                    root.EnumerateArray()
                    |> Seq.map (fun pullRequest ->
                        integerProperty "number" pullRequest
                        |> Result.bind (fun number ->
                            stringProperty "headRefName" pullRequest
                            |> Result.bind (fun headBranch ->
                                stringProperty "headRefOid" pullRequest
                                |> Result.map (fun headSha -> {
                                    Number = number
                                    HeadBranch = headBranch
                                    HeadSha = headSha
                                }))))
                    |> Seq.toList
                    |> sequenceResults)
            text

    let parseWorktreeCount (text: string) =
        text.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun line -> line.StartsWith("worktree ", StringComparison.Ordinal))
        |> Array.length

    let validateRegistrationWorktreeState registrationFileExists worktreeCount =
        if not registrationFileExists && worktreeCount > 1 then
            Error
                "The local stack registration is missing and multiple worktrees exist; stop to avoid misclassifying linked-worktree state."
        else
            Ok()

    let private setText (target: JsonObject) (name: string) (value: string) = target[name] <- JsonValue.Create(value)

    let private setInteger (target: JsonObject) (name: string) (value: int) = target[name] <- JsonValue.Create(value)

    let private setStringList (target: JsonObject) (name: string) (values: string list) =
        let array = JsonArray()
        values |> List.iter (fun value -> array.Add(JsonValue.Create(value)))
        target[name] <- array

    let reportJson
        (pullRequest: PullRequest)
        (trunkBranch: string)
        (startingBranch: string)
        (stackNumber: int option)
        (registrations: Registration list)
        (allRegistrations: Registration list)
        (dependencies: DependencyEvidence option)
        (classification: string)
        (reason: string)
        =
        let pullRequestJson = JsonObject()
        setInteger pullRequestJson "number" pullRequest.Number
        setText pullRequestJson "url" pullRequest.Url
        setText pullRequestJson "state" pullRequest.State
        setText pullRequestJson "headBranch" pullRequest.HeadBranch
        setText pullRequestJson "headSha" pullRequest.HeadSha
        setText pullRequestJson "baseBranch" pullRequest.BaseBranch

        match pullRequest.MergeCommitSha with
        | Some mergeCommitSha -> setText pullRequestJson "mergeCommitSha" mergeCommitSha
        | None -> pullRequestJson["mergeCommitSha"] <- null

        let report = JsonObject()
        setText report "classification" classification
        setText report "reason" reason
        setText report "trunkBranch" trunkBranch
        setText report "startingBranch" startingBranch
        report["pullRequest"] <- pullRequestJson

        match stackNumber with
        | Some number -> setInteger report "githubStackNumber" number
        | None -> report["githubStackNumber"] <- null

        let setRegistration (target: JsonArray) (value: Registration) =
            let registrationJson = JsonObject()
            setText registrationJson "trunkBranch" value.TrunkBranch
            setStringList registrationJson "branches" value.Branches

            target.Add registrationJson

        let localRegistrations = JsonArray()
        registrations |> List.iter (setRegistration localRegistrations)
        report["localRegistrations"] <- localRegistrations

        let relatedRegistrations =
            relatedLocalRegistrations pullRequest.HeadBranch allRegistrations registrations

        let relatedLocalRegistrationJson = JsonArray()
        relatedRegistrations |> List.iter (setRegistration relatedLocalRegistrationJson)
        report["relatedLocalRegistrations"] <- relatedLocalRegistrationJson

        let dependencyCheck, dependants, localDependants =
            match dependencies with
            | None -> "not-needed", [], []
            | Some evidence -> "complete", evidence.ActivePullRequests, evidence.LocalBranches

        setText report "dependencyCheck" dependencyCheck
        let dependentPullRequests = JsonArray()

        dependants
        |> List.iter (fun pullRequest ->
            let dependentPullRequestJson = JsonObject()
            setInteger dependentPullRequestJson "number" pullRequest.Number
            setText dependentPullRequestJson "headBranch" pullRequest.HeadBranch
            setText dependentPullRequestJson "headSha" pullRequest.HeadSha
            dependentPullRequests.Add dependentPullRequestJson)

        report["activeDependants"] <- dependentPullRequests
        setStringList report "activeLocalDependants" localDependants

        JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true))

module internal Commands =
    open Maintenance

    type CommandOutput = { ExitCode: int; StandardOutput: string; StandardError: string }

    type CommandRunner = string -> string -> string list -> Result<CommandOutput, string>

    let execute workingDirectory executable arguments =
        try
            let startInfo =
                ProcessStartInfo(
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                )

            startInfo.FileName <- executable
            arguments |> List.iter startInfo.ArgumentList.Add

            use childProcess = new Process(StartInfo = startInfo)

            if not (childProcess.Start()) then
                Error $"Could not start '{executable}'."
            else
                let standardOutput = StringBuilder()
                let standardError = StringBuilder()

                childProcess.OutputDataReceived.Add(fun eventArgs ->
                    if not (isNull eventArgs.Data) then
                        standardOutput.AppendLine(eventArgs.Data) |> ignore)

                childProcess.ErrorDataReceived.Add(fun eventArgs ->
                    if not (isNull eventArgs.Data) then
                        standardError.AppendLine(eventArgs.Data) |> ignore)

                childProcess.BeginOutputReadLine()
                childProcess.BeginErrorReadLine()
                childProcess.WaitForExit()

                Ok {
                    ExitCode = childProcess.ExitCode
                    StandardOutput = standardOutput.ToString()
                    StandardError = standardError.ToString()
                }
        with :? System.ComponentModel.Win32Exception as error ->
            Error $"Could not start '{executable}': {error.Message}"

    let run (commandRunner: CommandRunner) workingDirectory executable arguments =
        commandRunner workingDirectory executable arguments
        |> Result.bind (fun output ->
            if output.ExitCode = 0 then
                Ok output.StandardOutput
            else
                let detail = output.StandardError.Trim()
                Error $"'{executable}' exited {output.ExitCode}: {detail}")

    let private runGit commandRunner workingDirectory arguments =
        run commandRunner workingDirectory "git" arguments

    let private runGh commandRunner workingDirectory arguments =
        run commandRunner workingDirectory "gh" arguments

    let private readStartingBranch commandRunner root =
        runGit commandRunner root [ "rev-parse"; "--abbrev-ref"; "HEAD" ]
        |> Result.map (fun branch -> branch.Trim())
        |> Result.bind (fun branch ->
            if String.IsNullOrWhiteSpace branch || branch = "HEAD" then
                Error "The current checkout is detached; switch to a branch before classifying maintenance."
            else
                Ok branch)

    let private requireCleanWorktree commandRunner root =
        runGit commandRunner root [ "status"; "--porcelain"; "--untracked-files=all" ]
        |> Result.bind (fun status ->
            if String.IsNullOrWhiteSpace status then
                Ok()
            else
                Error
                    "The worktree is not clean. Stop without stashing or discarding changes; report the status and ask the user.")

    let private readPullRequest commandRunner (root: string) (number: int) : Result<PullRequest, string> =
        runGh commandRunner root [
            "pr"
            "view"
            string number
            "--json"
            "number,url,state,headRefName,headRefOid,baseRefName,mergeCommit"
        ]
        |> Result.bind parsePullRequest

    let private readRepository commandRunner root =
        runGh commandRunner root [ "repo"; "view"; "--json"; "nameWithOwner,defaultBranchRef" ]
        |> Result.bind parseRepository

    let private readGitHubStack commandRunner root owner repository number =
        let query =
            $"query {{ repository(owner:\"%s{owner}\",name:\"%s{repository}\") {{ pullRequest(number:%d{number}) {{ stack {{ number }} }} }} }}"

        runGh commandRunner root [ "api"; "graphql"; "-f"; $"query=%s{query}" ]
        |> Result.bind parseGitHubStack

    let private registrationPath commandRunner root =
        runGit commandRunner root [ "rev-parse"; "--git-path"; "gh-stack" ]
        |> Result.map (fun path ->
            let path = path.Trim()

            let fullPath =
                if Path.IsPathRooted path then
                    path
                else
                    Path.Combine(root, path)

            Path.GetFullPath fullPath)

    let private loadRegistrations expectedRepository path =
        try
            use reader = File.OpenText path

            parseRegistrations expectedRepository (reader.ReadToEnd())
            |> Result.map (fun registrations -> registrations, true)
        with
        | :? FileNotFoundException -> Ok([], false)
        | :? DirectoryNotFoundException -> Ok([], false)
        | :? IOException as error -> Error $"Could not read local stack registration: {error.Message}"
        | :? UnauthorizedAccessException as error -> Error $"Could not read local stack registration: {error.Message}"

    let private readRegistrations commandRunner root expectedRepository =
        result {
            let! path = registrationPath commandRunner root
            return! loadRegistrations expectedRepository path
        }

    let openPullRequests commandRunner root baseBranch =
        runGh commandRunner root [
            "pr"
            "list"
            "--state"
            "open"
            "--base"
            baseBranch
            "--limit"
            "1000"
            "--json"
            "number,headRefName,headRefOid"
        ]
        |> Result.bind (fun text ->
            parseOpenPullRequests text
            |> Result.bind (fun pullRequests ->
                if pullRequests.Length = 1000 then
                    Error $"The open PR list for base '{baseBranch}' reached its 1000-result limit."
                else
                    Ok pullRequests))

    let private pullRequestsForHead commandRunner root headBranch =
        runGh commandRunner root [
            "pr"
            "list"
            "--state"
            "open"
            "--head"
            headBranch
            "--limit"
            "1000"
            "--json"
            "number,headRefName,headRefOid"
        ]
        |> Result.bind (fun text ->
            parseOpenPullRequests text
            |> Result.bind (fun pullRequests ->
                if pullRequests.Length = 1000 then
                    Error $"The PR list for head '{headBranch}' reached its 1000-result limit."
                else
                    Ok pullRequests))

    let verifyGitHubStackDependants
        (commandRunner: CommandRunner)
        root
        owner
        repository
        stackNumber
        (dependencies: DependencyEvidence)
        =
        let belongsToStack (branch: string) (pullRequests: OpenPullRequest list) =
            match pullRequests with
            | [ pullRequest ] when pullRequest.HeadBranch = branch ->
                result {
                    let! candidateStackNumber =
                        readGitHubStack commandRunner root owner repository pullRequest.Number

                    return candidateStackNumber = Some stackNumber
                }
            | _ -> Ok false

        let rec verifyActiveDependants unverified verified remaining =
            match remaining with
            | [] -> Ok(List.distinct unverified, List.distinct verified)
            | pullRequest :: tail ->
                result {
                    let! candidateStackNumber =
                        readGitHubStack commandRunner root owner repository pullRequest.Number

                    if candidateStackNumber = Some stackNumber then
                        return! verifyActiveDependants unverified (pullRequest.HeadBranch :: verified) tail
                    else
                        return! verifyActiveDependants (pullRequest.HeadBranch :: unverified) verified tail
                }

        let rec verifyLocalBranches unverified verified remaining =
            match remaining with
            | [] -> Ok(List.distinct unverified)
            | branch :: tail when List.contains branch verified -> verifyLocalBranches unverified verified tail
            | branch :: tail ->
                result {
                    let! pullRequests = pullRequestsForHead commandRunner root branch
                    let! belongs = belongsToStack branch pullRequests

                    if belongs then
                        return! verifyLocalBranches unverified (branch :: verified) tail
                    else
                        return! verifyLocalBranches (branch :: unverified) verified tail
                }

        result {
            let! unverifiedPullRequests, verifiedPullRequests =
                verifyActiveDependants [] [] dependencies.ActivePullRequests

            let! unverifiedLocalBranches =
                verifyLocalBranches [] verifiedPullRequests dependencies.LocalBranches

            return List.append unverifiedPullRequests unverifiedLocalBranches |> List.distinct
        }

    let private verifyMergedPullRequestHead commandRunner root (pullRequest: PullRequest) =
        result {
            let! _ =
                runGit commandRunner root [
                    "fetch"
                    "--quiet"
                    "--no-tags"
                    "origin"
                    $"refs/pull/%d{pullRequest.Number}/head"
                ]

            let! fetchedHeadSha =
                runGit commandRunner root [ "rev-parse"; "FETCH_HEAD" ]
                |> Result.map (fun sha -> sha.Trim())

            if fetchedHeadSha <> pullRequest.HeadSha then
                return! Error "The merged PR head ref no longer matches the confirmed head SHA."

            return fetchedHeadSha
        }

    let localBranchDependants commandRunner root (pullRequest: PullRequest) confirmedHeadSha =
        runGit commandRunner root [ "branch"; "--contains"; confirmedHeadSha; "--format=%(refname:short)" ]
        |> Result.map (fun output ->
            output.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter (fun branch -> branch <> pullRequest.HeadBranch && branch <> pullRequest.BaseBranch)
            |> Array.toList)

    let dependentPullRequests
        (commandRunner: CommandRunner)
        root
        (mergedPullRequest: PullRequest)
        (trunkBranch: string)
        =
        result {
            let! directPullRequests =
                openPullRequests commandRunner root mergedPullRequest.HeadBranch

            let directDependants =
                directPullRequests
                |> List.choose (fun pullRequest ->
                    if pullRequest.Number = mergedPullRequest.Number then
                        None
                    else
                        Some pullRequest)

            let! trunkPullRequests = openPullRequests commandRunner root trunkBranch

            let candidates =
                trunkPullRequests
                |> List.filter (fun pullRequest -> pullRequest.Number <> mergedPullRequest.Number)

            let rec findDependants dependants remaining =
                match remaining with
                | [] -> Ok(List.rev dependants)
                | pullRequest :: tail ->
                    result {
                        let! _ =
                            runGit commandRunner root [
                                "fetch"
                                "--quiet"
                                "--no-tags"
                                "origin"
                                $"refs/pull/%d{pullRequest.Number}/head"
                            ]

                        let! fetchedHeadSha =
                            runGit commandRunner root [ "rev-parse"; "FETCH_HEAD" ]
                            |> Result.map (fun sha -> sha.Trim())

                        if fetchedHeadSha <> pullRequest.HeadSha then
                            return! Error $"PR #{pullRequest.Number} changed while checking dependent branches."
                        else
                            let! ancestry =
                                commandRunner root "git" [
                                    "merge-base"
                                    "--is-ancestor"
                                    mergedPullRequest.HeadSha
                                    fetchedHeadSha
                                ]

                            match ancestry.ExitCode with
                            | 0 -> return! findDependants (pullRequest :: dependants) tail
                            | 1 -> return! findDependants dependants tail
                            | exitCode ->
                                return!
                                    Error
                                        $"Ancestry check for PR #{pullRequest.Number} exited {exitCode}: {ancestry.StandardError.Trim()}"
                    }

            let! trunkDependants = findDependants [] candidates
            return List.append directDependants trunkDependants |> List.distinctBy _.Number
        }

    let checkDependencies commandRunner root (pullRequest: PullRequest) trunkBranch =
        result {
            let! confirmedHeadSha = verifyMergedPullRequestHead commandRunner root pullRequest

            let! localBranches =
                localBranchDependants commandRunner root pullRequest confirmedHeadSha

            let! activePullRequests =
                dependentPullRequests commandRunner root pullRequest trunkBranch

            return {
                ActivePullRequests = activePullRequests
                LocalBranches = localBranches
                UnverifiedStackBranches = []
            }
        }

    let private repositoryIdentity (nameWithOwner: string) =
        match nameWithOwner.Split('/') |> Array.toList with
        | [ owner; repository ] when owner <> "" && repository <> "" -> Ok(owner, repository)
        | _ -> Error $"GitHub returned an invalid repository name '{nameWithOwner}'."

    let collect (commandRunner: CommandRunner) (root: string) (pullRequestNumber: int) =
        result {
            do! requireCleanWorktree commandRunner root
            let! startingBranch = readStartingBranch commandRunner root
            let! pullRequest = readPullRequest commandRunner root pullRequestNumber

            do! validateMergedPullRequest pullRequest

            let! nameWithOwner, trunkBranch = readRepository commandRunner root
            let! owner, repository = repositoryIdentity nameWithOwner

            let! stackNumber =
                readGitHubStack commandRunner root owner repository pullRequest.Number

            let expectedRegistrationRepository = $"github.com:%s{nameWithOwner}"

            let! registrations, registrationFileExists =
                readRegistrations commandRunner root expectedRegistrationRepository

            let! worktreeCount =
                if registrationFileExists then
                    Ok 1
                else
                    runGit commandRunner root [ "worktree"; "list"; "--porcelain" ]
                    |> Result.map parseWorktreeCount

            do! validateRegistrationWorktreeState registrationFileExists worktreeCount

            let matchingRegistrations =
                registrations
                |> List.filter (fun registration -> registration.Branches |> List.contains pullRequest.HeadBranch)

            let! dependencyEvidence =
                match stackNumber, matchingRegistrations, pullRequest.BaseBranch = trunkBranch with
                | Some stackNumber, _, _ ->
                    result {
                        let! dependencies = checkDependencies commandRunner root pullRequest trunkBranch

                        let! unverifiedStackBranches =
                            verifyGitHubStackDependants commandRunner root owner repository stackNumber dependencies

                        return
                            Some {
                                dependencies with
                                    UnverifiedStackBranches = unverifiedStackBranches
                            }
                    }
                | None, _ :: _, _ -> checkDependencies commandRunner root pullRequest trunkBranch |> Result.map Some
                | None, [], false -> Ok None
                | None, [], true -> checkDependencies commandRunner root pullRequest trunkBranch |> Result.map Some

            let evidence = {
                TrunkBranch = trunkBranch
                BaseBranch = pullRequest.BaseBranch
                HeadBranch = pullRequest.HeadBranch
                GitHubStackNumber = stackNumber
                AllRegistrations = registrations
                MatchingRegistrations = matchingRegistrations
                Dependencies = dependencyEvidence
            }

            return
                startingBranch,
                pullRequest,
                trunkBranch,
                stackNumber,
                matchingRegistrations,
                registrations,
                dependencyEvidence,
                classify evidence
        }

module internal SelfTest =
    open Maintenance

    let run () =
        let assertEqual name expected actual =
            if expected <> actual then
                failwith $"Self-test failed: {name}. Expected %A{expected}, got %A{actual}."

        let commandOutput exitCode standardOutput standardError : Commands.CommandOutput = {
            ExitCode = exitCode
            StandardOutput = standardOutput
            StandardError = standardError
        }

        let command executable arguments exitCode standardOutput standardError =
            executable, arguments, commandOutput exitCode standardOutput standardError

        let scriptedRunner (expectedCommands: (string * string list * Commands.CommandOutput) list) =
            let mutable remainingCommands = expectedCommands
            let actualCommands = ResizeArray<string * string list>()

            let run
                (workingDirectory: string)
                (executable: string)
                (arguments: string list)
                : Result<Commands.CommandOutput, string> =
                let argumentText = String.Join(" ", arguments)
                actualCommands.Add((executable, arguments))

                match remainingCommands with
                | (expectedExecutable, expectedArguments, response) :: tail ->
                    remainingCommands <- tail

                    if executable = expectedExecutable && arguments = expectedArguments then
                        Ok response
                    else
                        Error $"Unexpected command in {workingDirectory}: {executable} {argumentText}"
                | [] -> Error $"Unexpected command in {workingDirectory}: {executable} {argumentText}"

            run, actualCommands, (fun () -> List.length remainingCommands)

        let assertScriptConsumed
            name
            expectedCommands
            (actualCommands: ResizeArray<string * string list>)
            remainingCount
            =
            assertEqual name expectedCommands (actualCommands |> Seq.toList)
            assertEqual $"{name}: all scripted responses were consumed" 0 (remainingCount ())

        let withTemporaryDirectory action =
            let path =
                Path.Combine(Path.GetTempPath(), $"Wilnaatahl-maintenance-selftest-{Guid.NewGuid():N}")

            Directory.CreateDirectory path |> ignore

            try
                action path
            finally
                Directory.Delete(path, true)

        let registration = { TrunkBranch = "main"; Branches = [ "feature/base"; "feature/top" ] }

        let evidence = {
            TrunkBranch = "main"
            BaseBranch = "main"
            HeadBranch = "feature/base"
            GitHubStackNumber = None
            AllRegistrations = []
            MatchingRegistrations = []
            Dependencies =
                Some {
                    ActivePullRequests = []
                    LocalBranches = []
                    UnverifiedStackBranches = []
                }
        }

        assertEqual "standalone PRs are classified without stack registration" Standalone (classify evidence)

        assertEqual
            "GitHub stack membership takes precedence over standalone checks"
            (GitHubStack 42)
            (classify { evidence with GitHubStackNumber = Some 42 })

        assertEqual
            "verified GitHub stack dependants do not require a local registration"
            (GitHubStack 42)
            (classify {
                evidence with
                    GitHubStackNumber = Some 42
                    Dependencies =
                        Some {
                            ActivePullRequests = [ { Number = 66; HeadBranch = "feature/top"; HeadSha = "child-sha" } ]
                            LocalBranches = []
                            UnverifiedStackBranches = []
                        }
            })

        assertEqual
            "dependants outside a GitHub stack stop classification"
            (Stop "Dependent branches are outside the GitHub stack: feature/external.")
            (classify {
                evidence with
                    GitHubStackNumber = Some 42
                    Dependencies =
                        Some {
                            ActivePullRequests = [
                                {
                                    Number = 67
                                    HeadBranch = "feature/external"
                                    HeadSha = "external-sha"
                                }
                            ]
                            LocalBranches = []
                            UnverifiedStackBranches = [ "feature/external" ]
                        }
            })

        assertEqual
            "local-only registrations are kept out of the standalone path"
            (LocalOnlyRegistration registration)
            (classify {
                evidence with
                    MatchingRegistrations = [ registration ]
                    AllRegistrations = [ registration ]
            })

        assertEqual
            "dependants inside a registered stack remain on its maintenance path"
            (LocalOnlyRegistration registration)
            (classify {
                evidence with
                    AllRegistrations = [ registration ]
                    MatchingRegistrations = [ registration ]
                    Dependencies =
                        Some {
                            ActivePullRequests = [ { Number = 66; HeadBranch = "feature/top"; HeadSha = "child-sha" } ]
                            LocalBranches = [ "feature/top" ]
                            UnverifiedStackBranches = []
                        }
            })

        assertEqual
            "dependants outside a registered stack stop before unstacking"
            (Stop "Dependent branches are outside the local registration: feature/external, feature/other.")
            (classify {
                evidence with
                    AllRegistrations = [ registration ]
                    MatchingRegistrations = [ registration ]
                    Dependencies =
                        Some {
                            ActivePullRequests = [
                                {
                                    Number = 67
                                    HeadBranch = "feature/external"
                                    HeadSha = "external-sha"
                                }
                            ]
                            LocalBranches = [ "feature/other" ]
                            UnverifiedStackBranches = []
                        }
            })

        let siblingRegistration = { TrunkBranch = "feature/base"; Branches = [ "feature/sibling" ] }

        assertEqual
            "a sibling registration using the merged branch as its trunk stops cleanup"
            (Stop "Another local registration uses the merged PR branch as its trunk.")
            (classify {
                evidence with
                    AllRegistrations = [ registration; siblingRegistration ]
                    MatchingRegistrations = [ registration ]
            })

        let overlappingRegistration = {
            TrunkBranch = "main"
            Branches = [ "feature/top"; "feature/sibling" ]
        }

        assertEqual
            "a registered branch shared with a sibling registration stops cleanup"
            (Stop "The local registration shares a branch with another registration.")
            (classify {
                evidence with
                    AllRegistrations = [ registration; overlappingRegistration ]
                    MatchingRegistrations = [ registration ]
            })

        assertEqual
            "open dependants without a registration stop cleanup"
            (Stop "Open dependent PRs have no verified stack registration: 12, 13.")
            (classify {
                evidence with
                    Dependencies =
                        Some {
                            ActivePullRequests = [
                                { Number = 12; HeadBranch = "feature/child-one"; HeadSha = "sha-12" }
                                { Number = 13; HeadBranch = "feature/child-two"; HeadSha = "sha-13" }
                            ]
                            LocalBranches = []
                            UnverifiedStackBranches = []
                        }
            })

        assertEqual
            "local dependent branches without a registration stop cleanup"
            (Stop "Local branches depend on this PR without a verified stack registration: feature/top.")
            (classify {
                evidence with
                    Dependencies =
                        Some {
                            ActivePullRequests = []
                            LocalBranches = [ "feature/top" ]
                            UnverifiedStackBranches = []
                        }
            })

        assertEqual
            "unverified dependencies stop cleanup"
            (Stop "Dependent PR and local branch checks were not run.")
            (classify { evidence with Dependencies = None })

        assertEqual
            "unregistered non-trunk PRs stop cleanup"
            (Stop "The PR has a non-trunk base but no verified stack registration.")
            (classify { evidence with BaseBranch = "feature/base"; Dependencies = None })

        assertEqual
            "ambiguous local registrations stop cleanup"
            (Stop "More than one local registration contains this PR branch.")
            (classify {
                evidence with
                    MatchingRegistrations = [ registration; registration ]
                    AllRegistrations = [ registration; registration ]
            })

        let registrationJson =
            """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[{"trunk":{"branch":"main"},"branches":[{"branch":"feature/base"},{"branch":"feature/top"}]}]}"""

        assertEqual
            "registration branches and trunk are parsed together"
            (Ok [ registration ])
            (parseRegistrations "github.com:bitsforthought/Wilnaatahl" registrationJson)

        assertEqual
            "registration for another repository is rejected"
            true
            (parseRegistrations "github.com:other/repository" registrationJson
             |> Result.isError)

        assertEqual
            "malformed local registration is rejected"
            true
            (parseRegistrations
                "github.com:bitsforthought/Wilnaatahl"
                """{"schemaVersion":2,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[]}"""
             |> Result.isError)

        assertEqual
            "null GitHub stack is classified as absent"
            (Ok None)
            (parseGitHubStack """{"data":{"repository":{"pullRequest":{"stack":null}}}}""")

        assertEqual
            "a GitHub stack number is parsed explicitly"
            (Ok(Some 42))
            (parseGitHubStack """{"data":{"repository":{"pullRequest":{"stack":{"number":42}}}}}""")

        assertEqual
            "PR list entries retain number, head branch, and head SHA"
            (Ok [ { Number = 1; HeadBranch = "feature/one"; HeadSha = "head-sha" } ])
            (parseOpenPullRequests """[{"number":1,"headRefName":"feature/one","headRefOid":"head-sha"}]""")

        assertEqual "malformed PR list is rejected" true (parseOpenPullRequests """{"number":1}""" |> Result.isError)

        assertEqual
            "worktree count includes porcelain entries"
            2
            (parseWorktreeCount "worktree C:\\repo\nHEAD abc\n\nworktree C:\\other\nHEAD def\n")

        assertEqual
            "missing registration is accepted in one worktree"
            (Ok())
            (validateRegistrationWorktreeState false 1)

        assertEqual
            "missing registration with linked worktrees stops classification"
            (Error
                "The local stack registration is missing and multiple worktrees exist; stop to avoid misclassifying linked-worktree state.")
            (validateRegistrationWorktreeState false 2)

        let pullRequestJson =
            """{"number":65,"url":"https://github.com/bitsforthought/Wilnaatahl/pull/65","state":"MERGED","headRefName":"feature/base","headRefOid":"head-sha","baseRefName":"main","mergeCommit":{"oid":"merge-sha"}}"""

        assertEqual
            "merged PR evidence retains its head and merge identities"
            {
                Number = 65
                Url = "https://github.com/bitsforthought/Wilnaatahl/pull/65"
                State = "MERGED"
                HeadBranch = "feature/base"
                HeadSha = "head-sha"
                BaseBranch = "main"
                MergeCommitSha = Some "merge-sha"
            }
            (parsePullRequest pullRequestJson |> Result.defaultWith failwith)

        let parsedPullRequest =
            parsePullRequest pullRequestJson |> Result.defaultWith failwith

        assertEqual "merged PR state passes validation" (Ok()) (validateMergedPullRequest parsedPullRequest)

        assertEqual
            "open PR state returns the precise validation error"
            (Error "PR #65 is in state OPEN, not MERGED.")
            (validateMergedPullRequest { parsedPullRequest with State = "OPEN" })

        let repositoryRoot = @"C:\repo"

        let mergedPullRequest = {
            Number = 65
            Url = "https://github.com/bitsforthought/Wilnaatahl/pull/65"
            State = "MERGED"
            HeadBranch = "feature/base"
            HeadSha = "merged-head"
            BaseBranch = "main"
            MergeCommitSha = Some "merge-commit"
        }

        let stackDependencyCommands = [
            command
                "gh"
                [
                    "api"
                    "graphql"
                    "-f"
                    "query=query { repository(owner:\"bitsforthought\",name:\"Wilnaatahl\") { pullRequest(number:66) { stack { number } } } }"
                ]
                0
                """{"data":{"repository":{"pullRequest":{"stack":{"number":42}}}}}"""
                ""
            command
                "gh"
                [
                    "api"
                    "graphql"
                    "-f"
                    "query=query { repository(owner:\"bitsforthought\",name:\"Wilnaatahl\") { pullRequest(number:68) { stack { number } } } }"
                ]
                0
                """{"data":{"repository":{"pullRequest":{"stack":{"number":99}}}}}"""
                ""
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--head"
                    "feature/local-child"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                """[{"number":67,"headRefName":"feature/local-child","headRefOid":"local-child-sha"}]"""
                ""
            command
                "gh"
                [
                    "api"
                    "graphql"
                    "-f"
                    "query=query { repository(owner:\"bitsforthought\",name:\"Wilnaatahl\") { pullRequest(number:67) { stack { number } } } }"
                ]
                0
                """{"data":{"repository":{"pullRequest":{"stack":{"number":42}}}}}"""
                ""
        ]

        let stackDependencyRunner, stackDependencyCalls, stackDependencyRemaining =
            scriptedRunner stackDependencyCommands

        assertEqual
            "GitHub stack verification keeps same-stack dependants and flags external PRs"
            (Ok [ "feature/external" ])
            (Commands.verifyGitHubStackDependants stackDependencyRunner repositoryRoot "bitsforthought" "Wilnaatahl" 42 {
                ActivePullRequests = [
                    { Number = 66; HeadBranch = "feature/child"; HeadSha = "child-sha" }
                    {
                        Number = 68
                        HeadBranch = "feature/external"
                        HeadSha = "external-sha"
                    }
                ]
                LocalBranches = [ "feature/local-child" ]
                UnverifiedStackBranches = []
            })

        assertScriptConsumed
            "GitHub stack dependant verification"
            (stackDependencyCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            stackDependencyCalls
            stackDependencyRemaining

        let staleStackBranchCommands = [
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--head"
                    "feature/stale-child"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                "[]"
                ""
        ]

        let staleStackBranchRunner, staleStackBranchCalls, staleStackBranchRemaining =
            scriptedRunner staleStackBranchCommands

        assertEqual
            "a local branch with only historical PRs is not accepted as a stack member"
            (Ok [ "feature/stale-child" ])
            (Commands.verifyGitHubStackDependants staleStackBranchRunner repositoryRoot "bitsforthought" "Wilnaatahl" 42 {
                ActivePullRequests = []
                LocalBranches = [ "feature/stale-child" ]
                UnverifiedStackBranches = []
            })

        assertScriptConsumed
            "historical stack branch check"
            (staleStackBranchCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            staleStackBranchCalls
            staleStackBranchRemaining

        let ambiguousStackBranchCommands = [
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--head"
                    "feature/ambiguous-child"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                """[{"number":67,"headRefName":"feature/ambiguous-child","headRefOid":"first-sha"},{"number":68,"headRefName":"feature/ambiguous-child","headRefOid":"second-sha"}]"""
                ""
        ]

        let ambiguousStackBranchRunner, ambiguousStackBranchCalls, ambiguousStackBranchRemaining =
            scriptedRunner ambiguousStackBranchCommands

        assertEqual
            "multiple open PRs for a local branch do not establish unique stack membership"
            (Ok [ "feature/ambiguous-child" ])
            (Commands.verifyGitHubStackDependants
                ambiguousStackBranchRunner
                repositoryRoot
                "bitsforthought"
                "Wilnaatahl"
                42
                {
                    ActivePullRequests = []
                    LocalBranches = [ "feature/ambiguous-child" ]
                    UnverifiedStackBranches = []
                })

        assertScriptConsumed
            "ambiguous stack branch check"
            (ambiguousStackBranchCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            ambiguousStackBranchCalls
            ambiguousStackBranchRemaining

        let mergedHeadCommands = [
            command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/65/head" ] 0 "" ""
            command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "merged-head\n" ""
            command
                "git"
                [ "branch"; "--contains"; "merged-head"; "--format=%(refname:short)" ]
                0
                "feature/base\nmain\n"
                ""
        ]

        let directDependantCommands =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    """[{"number":66,"headRefName":"feature/child","headRefOid":"child-head"}]"""
                    ""
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "main"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
            ]

        let directRunner, directCalls, directRemaining =
            scriptedRunner directDependantCommands

        assertEqual
            "direct-base open PRs are reported without a trunk-wide query"
            (Ok {
                ActivePullRequests = [ { Number = 66; HeadBranch = "feature/child"; HeadSha = "child-head" } ]
                LocalBranches = []
                UnverifiedStackBranches = []
            })
            (Commands.checkDependencies directRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "direct dependant query"
            (directDependantCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            directCalls
            directRemaining

        let retargetedDependantCommands =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "main"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    """[{"number":66,"headRefName":"feature/child","headRefOid":"child-head"}]"""
                    ""
                command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/66/head" ] 0 "" ""
                command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "child-head\n" ""
                command "git" [ "merge-base"; "--is-ancestor"; "merged-head"; "child-head" ] 0 "" ""
            ]

        let retargetedRunner, retargetedCalls, retargetedRemaining =
            scriptedRunner retargetedDependantCommands

        assertEqual
            "trunk-retargeted PRs descended from the merged head remain dependencies"
            (Ok {
                ActivePullRequests = [ { Number = 66; HeadBranch = "feature/child"; HeadSha = "child-head" } ]
                LocalBranches = []
                UnverifiedStackBranches = []
            })
            (Commands.checkDependencies retargetedRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "retargeted dependant query"
            (retargetedDependantCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            retargetedCalls
            retargetedRemaining

        let localDependantCommands = [
            command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/65/head" ] 0 "" ""
            command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "merged-head\n" ""
            command
                "git"
                [ "branch"; "--contains"; "merged-head"; "--format=%(refname:short)" ]
                0
                "feature/base\nfeature/top\nmain\n"
                ""
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--base"
                    "feature/base"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                "[]"
                ""
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--base"
                    "main"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                "[]"
                ""
        ]

        let localRunner, localCalls, localRemaining = scriptedRunner localDependantCommands

        assertEqual
            "local branches containing the merged head are surfaced"
            (Ok {
                ActivePullRequests = []
                LocalBranches = [ "feature/top" ]
                UnverifiedStackBranches = []
            })
            (Commands.checkDependencies localRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "local dependant query"
            (localDependantCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            localCalls
            localRemaining

        let wrongMergedHeadCommands = [
            command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/65/head" ] 0 "" ""
            command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "newer-head\n" ""
        ]

        let wrongHeadRunner, wrongHeadCalls, wrongHeadRemaining =
            scriptedRunner wrongMergedHeadCommands

        assertEqual
            "a changed merged PR head fails before dependant checks"
            (Error "The merged PR head ref no longer matches the confirmed head SHA.")
            (Commands.checkDependencies wrongHeadRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "merged-head verification"
            (wrongMergedHeadCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            wrongHeadCalls
            wrongHeadRemaining

        let ancestryCommands exitCode errorText =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "main"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    """[{"number":66,"headRefName":"feature/child","headRefOid":"child-head"}]"""
                    ""
                command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/66/head" ] 0 "" ""
                command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "child-head\n" ""
                command "git" [ "merge-base"; "--is-ancestor"; "merged-head"; "child-head" ] exitCode "" errorText
            ]

        let notAncestorCommands = ancestryCommands 1 "not an ancestor"

        let notAncestorRunner, notAncestorCalls, notAncestorRemaining =
            scriptedRunner notAncestorCommands

        assertEqual
            "ancestry exit status one means the open PR is not a dependant"
            (Ok {
                ActivePullRequests = []
                LocalBranches = []
                UnverifiedStackBranches = []
            })
            (Commands.checkDependencies notAncestorRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "negative ancestry query"
            (notAncestorCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            notAncestorCalls
            notAncestorRemaining

        let failedAncestryCommands = ancestryCommands 128 "object database unavailable"

        let failedAncestryRunner, failedAncestryCalls, failedAncestryRemaining =
            scriptedRunner failedAncestryCommands

        assertEqual
            "ancestry failures other than zero or one stop classification"
            (Error "Ancestry check for PR #66 exited 128: object database unavailable")
            (Commands.checkDependencies failedAncestryRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "failed ancestry query"
            (failedAncestryCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            failedAncestryCalls
            failedAncestryRemaining

        let mismatchedCandidateCommands =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "main"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    """[{"number":66,"headRefName":"feature/child","headRefOid":"queried-head"}]"""
                    ""
                command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/66/head" ] 0 "" ""
                command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "changed-head\n" ""
            ]

        let mismatchedCandidateRunner, mismatchedCandidateCalls, mismatchedCandidateRemaining =
            scriptedRunner mismatchedCandidateCommands

        assertEqual
            "a dependent PR changing during ancestry checking fails closed"
            (Error "PR #66 changed while checking dependent branches.")
            (Commands.checkDependencies mismatchedCandidateRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "dependent-head verification"
            (mismatchedCandidateCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            mismatchedCandidateCalls
            mismatchedCandidateRemaining

        let capEntries =
            [ 1..1000 ]
            |> List.map (fun number -> {|
                number = number
                headRefName = $"feature/{number}"
                headRefOid = $"head-{number}"
            |})
            |> JsonSerializer.Serialize

        let capCommands = [
            command
                "gh"
                [
                    "pr"
                    "list"
                    "--state"
                    "open"
                    "--base"
                    "main"
                    "--limit"
                    "1000"
                    "--json"
                    "number,headRefName,headRefOid"
                ]
                0
                capEntries
                ""
        ]

        let capRunner, capCalls, capRemaining = scriptedRunner capCommands

        assertEqual
            "a full 1000-result PR page stops dependent classification"
            (Error "The open PR list for base 'main' reached its 1000-result limit.")
            (Commands.openPullRequests capRunner repositoryRoot "main")

        assertScriptConsumed
            "PR result cap"
            (capCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            capCalls
            capRemaining

        let failedQueryCommands =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    1
                    ""
                    "API unavailable\n"
            ]

        let failedQueryRunner, failedQueryCalls, failedQueryRemaining =
            scriptedRunner failedQueryCommands

        assertEqual
            "GitHub dependency-query failures remain explicit"
            (Error "'gh' exited 1: API unavailable")
            (Commands.checkDependencies failedQueryRunner repositoryRoot mergedPullRequest "main")

        assertScriptConsumed
            "failed GitHub query"
            (failedQueryCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            failedQueryCalls
            failedQueryRemaining

        let dirtyWorktreeCommands = [
            command "git" [ "status"; "--porcelain"; "--untracked-files=all" ] 0 " M unrelated.txt\n" ""
        ]

        let dirtyRunner, dirtyCalls, dirtyRemaining = scriptedRunner dirtyWorktreeCommands

        assertEqual
            "dirty worktrees stop before any maintenance discovery"
            (Error
                "The worktree is not clean. Stop without stashing or discarding changes; report the status and ask the user.")
            (Commands.collect dirtyRunner repositoryRoot 65)

        assertScriptConsumed
            "dirty worktree guard"
            (dirtyWorktreeCommands
             |> List.map (fun (executable, arguments, _) -> executable, arguments))
            dirtyCalls
            dirtyRemaining

        let collectedPullRequestJson =
            """{"number":65,"url":"https://github.com/bitsforthought/Wilnaatahl/pull/65","state":"MERGED","headRefName":"feature/base","headRefOid":"merged-head","baseRefName":"main","mergeCommit":{"oid":"merge-commit"}}"""

        let collectPrefix stackJson = [
            command "git" [ "status"; "--porcelain"; "--untracked-files=all" ] 0 "" ""
            command "git" [ "rev-parse"; "--abbrev-ref"; "HEAD" ] 0 "maintenance\n" ""
            command
                "gh"
                [
                    "pr"
                    "view"
                    "65"
                    "--json"
                    "number,url,state,headRefName,headRefOid,baseRefName,mergeCommit"
                ]
                0
                collectedPullRequestJson
                ""
            command
                "gh"
                [ "repo"; "view"; "--json"; "nameWithOwner,defaultBranchRef" ]
                0
                """{"nameWithOwner":"bitsforthought/Wilnaatahl","defaultBranchRef":{"name":"main"}}"""
                ""
            command
                "gh"
                [
                    "api"
                    "graphql"
                    "-f"
                    "query=query { repository(owner:\"bitsforthought\",name:\"Wilnaatahl\") { pullRequest(number:65) { stack { number } } } }"
                ]
                0
                stackJson
                ""
            command "git" [ "rev-parse"; "--git-path"; "gh-stack" ] 0 "gh-stack\n" ""
        ]

        let registrationFileText =
            """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[]}"""

        let emptyDependencyCommands =
            mergedHeadCommands
            @ [
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "feature/base"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
                command
                    "gh"
                    [
                        "pr"
                        "list"
                        "--state"
                        "open"
                        "--base"
                        "main"
                        "--limit"
                        "1000"
                        "--json"
                        "number,headRefName,headRefOid"
                    ]
                    0
                    "[]"
                    ""
            ]

        withTemporaryDirectory (fun path ->
            File.WriteAllText(Path.Combine(path, "gh-stack"), registrationFileText)

            let stackCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":{"number":42}}}}}"""
                @ emptyDependencyCommands

            let stackRunner, stackCalls, stackRemaining = scriptedRunner stackCommands

            assertEqual
                "GitHub stack classification checks dependants before routing"
                (Ok(
                    Some {
                        ActivePullRequests = []
                        LocalBranches = []
                        UnverifiedStackBranches = []
                    },
                    GitHubStack 42
                ))
                (Commands.collect stackRunner path 65
                 |> Result.map (fun (_, _, _, _, _, _, dependencies, classification) -> dependencies, classification))

            assertScriptConsumed
                "GitHub stack routing"
                (stackCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                stackCalls
                stackRemaining)

        withTemporaryDirectory (fun path ->
            File.WriteAllText(Path.Combine(path, "gh-stack"), registrationFileText)

            let externalStackDependantCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":{"number":42}}}}}"""
                @ mergedHeadCommands
                @ [
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "feature/base"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        """[{"number":66,"headRefName":"feature/external","headRefOid":"child-head"}]"""
                        ""
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "main"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        "[]"
                        ""
                    command
                        "gh"
                        [
                            "api"
                            "graphql"
                            "-f"
                            "query=query { repository(owner:\"bitsforthought\",name:\"Wilnaatahl\") { pullRequest(number:66) { stack { number } } } }"
                        ]
                        0
                        """{"data":{"repository":{"pullRequest":{"stack":{"number":99}}}}}"""
                        ""
                ]

            let externalRunner, externalCalls, externalRemaining =
                scriptedRunner externalStackDependantCommands

            assertEqual
                "collect stops when an active dependant belongs to another GitHub stack"
                (Ok(
                    Some {
                        ActivePullRequests = [
                            {
                                Number = 66
                                HeadBranch = "feature/external"
                                HeadSha = "child-head"
                            }
                        ]
                        LocalBranches = []
                        UnverifiedStackBranches = [ "feature/external" ]
                    },
                    Stop "Dependent branches are outside the GitHub stack: feature/external."
                ))
                (Commands.collect externalRunner path 65
                 |> Result.map (fun (_, _, _, _, _, _, dependencies, classification) -> dependencies, classification))

            assertScriptConsumed
                "external GitHub stack dependant routing"
                (externalStackDependantCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                externalCalls
                externalRemaining)

        withTemporaryDirectory (fun path ->
            File.WriteAllText(Path.Combine(path, "gh-stack"), registrationFileText)

            let standaloneCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ emptyDependencyCommands

            let standaloneRunner, standaloneCalls, standaloneRemaining =
                scriptedRunner standaloneCommands

            assertEqual
                "standalone classification verifies local and GitHub dependencies"
                (Ok(
                    Some {
                        ActivePullRequests = []
                        LocalBranches = []
                        UnverifiedStackBranches = []
                    },
                    Standalone
                ))
                (Commands.collect standaloneRunner path 65
                 |> Result.map (fun (_, _, _, _, _, _, dependencies, classification) -> dependencies, classification))

            assertScriptConsumed
                "standalone routing"
                (standaloneCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                standaloneCalls
                standaloneRemaining)

        withTemporaryDirectory (fun path ->
            File.WriteAllText(
                Path.Combine(path, "gh-stack"),
                """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[{"trunk":{"branch":"main"},"branches":[{"branch":"feature/unrelated"}]}]}"""
            )

            let unrelatedRegistrationCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ emptyDependencyCommands

            let unrelatedRunner, unrelatedCalls, unrelatedRemaining =
                scriptedRunner unrelatedRegistrationCommands

            assertEqual
                "a registration without the PR branch does not classify it as local-only"
                (Ok(([], Standalone)))
                (Commands.collect unrelatedRunner path 65
                 |> Result.map (fun (_, _, _, _, matching, _, _, classification) -> matching, classification))

            assertScriptConsumed
                "unrelated registration routing"
                (unrelatedRegistrationCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                unrelatedCalls
                unrelatedRemaining)

        withTemporaryDirectory (fun path ->
            let localRegistration = { TrunkBranch = "main"; Branches = [ "feature/base"; "feature/top" ] }

            File.WriteAllText(
                Path.Combine(path, "gh-stack"),
                """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[{"trunk":{"branch":"main"},"branches":[{"branch":"feature/base"},{"branch":"feature/top"}]}]}"""
            )

            let registeredCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ [
                    command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/65/head" ] 0 "" ""
                    command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "merged-head\n" ""
                    command
                        "git"
                        [ "branch"; "--contains"; "merged-head"; "--format=%(refname:short)" ]
                        0
                        "feature/base\nfeature/top\nmain\n"
                        ""
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "feature/base"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        """[{"number":66,"headRefName":"feature/top","headRefOid":"child-head"}]"""
                        ""
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "main"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        "[]"
                        ""
                ]

            let registeredRunner, registeredCalls, registeredRemaining =
                scriptedRunner registeredCommands

            assertEqual
                "registered dependants are checked against the full local branch list"
                (Ok(Some localRegistration, LocalOnlyRegistration localRegistration))
                (Commands.collect registeredRunner path 65
                 |> Result.map (fun (_, _, _, _, matched, _, _, classification) ->
                     List.tryHead matched, classification))

            assertScriptConsumed
                "registered dependency routing"
                (registeredCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                registeredCalls
                registeredRemaining)

        withTemporaryDirectory (fun path ->
            File.WriteAllText(
                Path.Combine(path, "gh-stack"),
                """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[{"trunk":{"branch":"main"},"branches":[{"branch":"feature/base"},{"branch":"feature/top"}]}]}"""
            )

            let externalDependentCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ [
                    command "git" [ "fetch"; "--quiet"; "--no-tags"; "origin"; "refs/pull/65/head" ] 0 "" ""
                    command "git" [ "rev-parse"; "FETCH_HEAD" ] 0 "merged-head\n" ""
                    command
                        "git"
                        [ "branch"; "--contains"; "merged-head"; "--format=%(refname:short)" ]
                        0
                        "feature/base\nfeature/external\nmain\n"
                        ""
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "feature/base"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        """[{"number":67,"headRefName":"feature/external","headRefOid":"external-head"}]"""
                        ""
                    command
                        "gh"
                        [
                            "pr"
                            "list"
                            "--state"
                            "open"
                            "--base"
                            "main"
                            "--limit"
                            "1000"
                            "--json"
                            "number,headRefName,headRefOid"
                        ]
                        0
                        "[]"
                        ""
                ]

            let externalRunner, externalCalls, externalRemaining =
                scriptedRunner externalDependentCommands

            assertEqual
                "external direct dependants stop local stack cleanup"
                (Ok(Stop "Dependent branches are outside the local registration: feature/external."))
                (Commands.collect externalRunner path 65
                 |> Result.map (fun (_, _, _, _, _, _, _, classification) -> classification))

            assertScriptConsumed
                "external dependant routing"
                (externalDependentCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                externalCalls
                externalRemaining)

        withTemporaryDirectory (fun path ->
            let localRegistrationJson =
                """{"schemaVersion":1,"repository":"github.com:bitsforthought/Wilnaatahl","stacks":[{"trunk":{"branch":"main"},"branches":[{"branch":"feature/base"}]},{"trunk":{"branch":"main"},"branches":[{"branch":"feature/base"},{"branch":"feature/sibling"}]}]}"""

            File.WriteAllText(Path.Combine(path, "gh-stack"), localRegistrationJson)

            let ambiguousRegistrationCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ emptyDependencyCommands

            let ambiguousRunner, ambiguousCalls, ambiguousRemaining =
                scriptedRunner ambiguousRegistrationCommands

            assertEqual
                "collect stops when multiple local registrations contain the merged branch"
                (Ok(Stop "More than one local registration contains this PR branch."))
                (Commands.collect ambiguousRunner path 65
                 |> Result.map (fun (_, _, _, _, _, _, _, classification) -> classification))

            assertScriptConsumed
                "ambiguous registration routing"
                (ambiguousRegistrationCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                ambiguousCalls
                ambiguousRemaining)

        withTemporaryDirectory (fun path ->
            let missingRegistrationCommands =
                collectPrefix """{"data":{"repository":{"pullRequest":{"stack":null}}}}"""
                @ [
                    command
                        "git"
                        [ "worktree"; "list"; "--porcelain" ]
                        0
                        "worktree C:\\repo\nHEAD abc\n\nworktree C:\\linked\nHEAD def\n"
                        ""
                ]

            let missingRegistrationRunner, missingRegistrationCalls, missingRegistrationRemaining =
                scriptedRunner missingRegistrationCommands

            assertEqual
                "missing registration metadata with linked worktrees fails closed"
                (Error
                    "The local stack registration is missing and multiple worktrees exist; stop to avoid misclassifying linked-worktree state.")
                (Commands.collect missingRegistrationRunner path 65)

            assertScriptConsumed
                "missing registration worktree guard"
                (missingRegistrationCommands
                 |> List.map (fun (executable, arguments, _) -> executable, arguments))
                missingRegistrationCalls
                missingRegistrationRemaining)

        let report =
            reportJson
                (parsePullRequest pullRequestJson |> Result.defaultWith failwith)
                "main"
                "maintenance"
                None
                [ registration; siblingRegistration ]
                [ registration; siblingRegistration ]
                (Some {
                    ActivePullRequests = [ { Number = 66; HeadBranch = "feature/top"; HeadSha = "child-sha" } ]
                    LocalBranches = [ "feature/top" ]
                    UnverifiedStackBranches = []
                })
                "stop"
                "Registration details are retained."

        use reportDocument = JsonDocument.Parse report

        assertEqual
            "classification output records the selected path"
            "stop"
            (reportDocument.RootElement.GetProperty("classification").GetString())

        assertEqual
            "classification output distinguishes completed dependency checks"
            "complete"
            (reportDocument.RootElement.GetProperty("dependencyCheck").GetString())

        assertEqual
            "classification output retains PR identity"
            65
            (reportDocument.RootElement.GetProperty("pullRequest").GetProperty("number").GetInt32())

        let firstDependent =
            reportDocument.RootElement.GetProperty("activeDependants").EnumerateArray()
            |> Seq.head

        assertEqual
            "classification output retains every matching registration"
            2
            (reportDocument.RootElement.GetProperty("localRegistrations").GetArrayLength())

        assertEqual
            "classification output retains dependent PR branch identity"
            "feature/top"
            (firstDependent.GetProperty("headBranch").GetString())

        assertEqual
            "classification output reports sibling registrations"
            1
            (reportDocument.RootElement.GetProperty("relatedLocalRegistrations").GetArrayLength())

        printfn "Self-test: passed."

module internal CommandLine =
    let parseArguments arguments =
        match arguments with
        | [ "--self-test" ] -> Ok None
        | [ pullRequestNumber ] ->
            match Int32.TryParse pullRequestNumber with
            | true, number when number > 0 -> Ok(Some number)
            | _ -> Error "The PR number must be a positive integer."
        | _ -> Error "Usage: PostMergeMaintenance.fsx <merged-pr-number> | --self-test"

let scriptArguments = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

match CommandLine.parseArguments scriptArguments with
| Error error ->
    eprintfn "%s" error
    exit 2
| Ok None -> SelfTest.run ()
| Ok(Some pullRequestNumber) ->
    let repositoryRoot = Path.GetDirectoryName(Path.GetFullPath __SOURCE_DIRECTORY__)

    match Commands.collect Commands.execute repositoryRoot pullRequestNumber with
    | Error error ->
        eprintfn "%s" error
        exit 2
    | Ok(startingBranch,
         pullRequest,
         trunkBranch,
         stackNumber,
         registrations,
         allRegistrations,
         dependencies,
         classification) ->
        let classificationName, reason, shouldStop =
            match classification with
            | Maintenance.Standalone -> "standalone", "No stack registration or active dependants were found.", false
            | Maintenance.LocalOnlyRegistration _ ->
                "local-only-registration", "A local registration contains the PR branch.", false
            | Maintenance.GitHubStack _ -> "github-stack", "GitHub reports that the PR belongs to a stack.", false
            | Maintenance.Stop message -> "stop", message, true

        printfn
            "%s"
            (Maintenance.reportJson
                pullRequest
                trunkBranch
                startingBranch
                stackNumber
                registrations
                allRegistrations
                dependencies
                classificationName
                reason)

        if shouldStop then
            exit 2
