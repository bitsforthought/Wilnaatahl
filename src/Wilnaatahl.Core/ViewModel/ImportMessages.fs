namespace Wilnaatahl.ViewModel

open Wilnaatahl.Persistence

/// Helpers for converting ImportError values into user-facing messages.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ImportError =
    let toMessage (error: ImportError) : string =
        match error with
        | InvalidJson detail -> $"Could not parse the file as JSON: {detail}"
        | EmptyPeopleArray -> "The file contains no people."

/// Helpers for converting ImportWarning values into user-facing messages and summaries.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ImportWarning =
    let toMessage (warning: ImportWarning) : string =
        match warning with
        | UnresolvedCoupleId(personName, coupleId) ->
            $"{personName} references couple #{coupleId} which does not exist; treated as a root."
        | UnresolvedMember(coupleId, memberId) ->
            $"Couple #{coupleId} references person #{memberId} which does not exist; couple dropped."
        | UnresolvedWilpId(personName, wilpId) ->
            $"{personName} references wilp #{wilpId} which does not exist or is unusable; Wilp left unset."
        | UnparseableDate(personName, fieldName, rawValue) ->
            $"{personName}: could not parse {fieldName} value '{rawValue}'."
        | UnparsableCoupleDate(coupleId, rawValue) ->
            $"Couple #{coupleId}: could not parse dateOfUnion value '{rawValue}'."
        | DuplicatePersonId id -> $"Duplicate person id #{id}; only the first occurrence was kept."
        | DuplicateCoupleId id -> $"Duplicate couple id #{id}; only the first occurrence was kept."
        | DuplicateWilpId id -> $"Duplicate wilp id #{id}; only the first occurrence was kept."
        | WilpMissingPdeek id -> $"Wilp #{id} has no pdeek; dropped."
        | WilpMissingNameAndPdeek id -> $"Wilp #{id} has neither name nor pdeek; dropped."
        | UnknownPdeek(wilpId, rawPdeek) -> $"Wilp #{wilpId} has unrecognized pdeek '{rawPdeek}'; dropped."

    /// Returns "" for an empty list. Otherwise returns a comma-separated summary
    /// like "3 unresolved parent couples, 1 unparseable date, 2 dropped huwilp".
    /// Pluralization is explicit per category so that wilp-related categories use
    /// the correct Gitxsan plural ("huwilp") instead of the English "+s".
    let summary (warnings: ImportWarning list) : string =
        if List.isEmpty warnings then
            ""
        else
            let category warning =
                match warning with
                | UnresolvedCoupleId _ -> "unresolved parent couple", "unresolved parent couples"
                | UnresolvedMember _ -> "dropped couple", "dropped couples"
                | UnresolvedWilpId _ -> "unresolved wilp", "unresolved huwilp"
                | UnparseableDate _ -> "unparseable date", "unparseable dates"
                | UnparsableCoupleDate _ -> "unparseable couple date", "unparseable couple dates"
                | DuplicatePersonId _ -> "duplicate person id", "duplicate person ids"
                | DuplicateCoupleId _ -> "duplicate couple id", "duplicate couple ids"
                | DuplicateWilpId _ -> "duplicate wilp id", "duplicate wilp ids"
                | WilpMissingPdeek _
                | WilpMissingNameAndPdeek _
                | UnknownPdeek _ -> "dropped wilp", "dropped huwilp"

            // Preserve first-seen order so the summary is deterministic.
            let counts =
                warnings
                |> List.fold
                    (fun (acc: ((string * string) * int) list) w ->
                        let key = category w

                        match List.tryFindIndex (fun (k, _) -> k = key) acc with
                        | Some i -> acc |> List.mapi (fun j (k, n) -> if j = i then (k, n + 1) else (k, n))
                        | None -> acc @ [ (key, 1) ])
                    []

            counts
            |> List.map (fun ((singular, plural), n) ->
                let label = if n = 1 then singular else plural
                $"{n} {label}")
            |> String.concat ", "
