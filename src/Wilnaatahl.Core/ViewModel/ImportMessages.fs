namespace Wilnaatahl.ViewModel

open Wilnaatahl.Model
open Wilnaatahl.Persistence

/// Helpers for converting ImportError values into user-facing messages.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ImportError =
    let toMessage (locale: Locale) (error: ImportError) : string =
        match locale with
        | En ->
            match error with
            | InvalidJson detail -> $"Could not parse the file as JSON: {detail}"
            | EmptyPeopleArray -> "The file contains no people."

/// Helpers for converting ImportWarning values into user-facing messages and summaries.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ImportWarning =
    let toMessage (locale: Locale) (warning: ImportWarning) : string =
        match locale with
        | En ->
            match warning with
            | UnresolvedCoupleId(personName, coupleId) ->
                $"{personName} references couple #{coupleId} which does not exist; treated as a root."
            | UnresolvedMember(coupleId, memberId) ->
                $"Couple #{coupleId} references person #{memberId} which does not exist; couple dropped."
            | SelfCoupledMember(coupleId, memberId) ->
                $"Couple #{coupleId} lists person #{memberId} as both members; couple dropped."
            | UnresolvedWilpId(personName, wilpId) ->
                $"{personName} references wilp #{wilpId} which does not exist or is unusable; Wilp left unset."
            | UnresolvedBirthWilpId(personName, wilpId) ->
                $"{personName} references birth wilp #{wilpId} which does not exist or is unusable; birth Wilp left unset."
            | BirthWilpNotNamed(personName, wilpId) ->
                $"{personName} references birth wilp #{wilpId} which has no name; birth Wilp left unset."
            | IgnoredKinshipNote personName ->
                $"{personName} has both a resolved Wilp and a kinship note; the note was ignored."
            | UnparseableDate(personName, fieldName, rawValue) ->
                $"{personName}: could not parse {fieldName} value '{rawValue}'."
            | UnparsableCoupleDate(coupleId, rawValue) ->
                $"Couple #{coupleId}: could not parse dateOfUnion value '{rawValue}'."
            | DuplicatePersonId id -> $"Duplicate person id #{id}; only the first occurrence was kept."
            | DuplicateCoupleId id -> $"Duplicate couple id #{id}; only the first occurrence was kept."
            | DuplicateWilpId id -> $"Duplicate wilp id #{id}; only the first occurrence was kept."
            | DuplicateNameId id -> $"Duplicate name id #{id}; only the first occurrence was kept."
            | DuplicateNameText text -> $"Duplicate name '{text}'; the redundant entry was merged."
            | UnresolvedNameId(personId, nameId) ->
                $"Person #{personId} holds name #{nameId} which does not exist; holding dropped."
            | UnresolvedNameHolder(nameId, personId) ->
                $"Name #{nameId} is held by person #{personId} which does not exist; holding dropped."
            | UnparseableNameDate(nameId, personId, rawValue) ->
                $"Name #{nameId} held by person #{personId} has an unparseable date '{rawValue}'; date ignored."
            | UnorderedNameHolding(nameId, personId) ->
                $"Name #{nameId} held by person #{personId} has no order and no usable date; sorted alphabetically."
            | UnheldName(nameId, text) -> $"Name #{nameId} '{text}' is held by nobody; dropped."
            | WilpMissingPdeek id -> $"Wilp #{id} has no pdeek; dropped."
            | WilpMissingNameAndPdeek id -> $"Wilp #{id} has neither name nor pdeek; dropped."
            | UnknownPdeek(wilpId, rawPdeek) -> $"Wilp #{wilpId} has unrecognized pdeek '{rawPdeek}'; dropped."
            | ConflictingWilpPdeek(wilpId, wilpName, pdeek) ->
                $"Wilp #{wilpId} '{wilpName}' has pdeek {Pdeek.displayName pdeek} but another wilp of the same name has a different pdeek; dropped."

    /// Returns "" for an empty list. Otherwise returns a comma-separated summary
    /// like "3 unresolved parent couples, 1 unparseable date, 2 dropped huwilp".
    /// Pluralization is explicit per category so that wilp-related categories use
    /// the correct Gitxsan plural ("huwilp") instead of the English "+s".
    let summary (locale: Locale) (warnings: ImportWarning list) : string =
        match locale with
        | En ->
            if List.isEmpty warnings then
                ""
            else
                let category warning =
                    match warning with
                    | UnresolvedCoupleId _ -> "unresolved parent couple", "unresolved parent couples"
                    | UnresolvedMember _ -> "dropped couple", "dropped couples"
                    | SelfCoupledMember _ -> "self-coupled couple", "self-coupled couples"
                    | UnresolvedWilpId _ -> "unresolved wilp", "unresolved huwilp"
                    | UnresolvedBirthWilpId _
                    | BirthWilpNotNamed _ -> "unresolved birth wilp", "unresolved birth huwilp"
                    | IgnoredKinshipNote _ -> "ignored kinship note", "ignored kinship notes"
                    | UnparseableDate _ -> "unparseable date", "unparseable dates"
                    | UnparsableCoupleDate _ -> "unparseable couple date", "unparseable couple dates"
                    | DuplicatePersonId _ -> "duplicate person id", "duplicate person ids"
                    | DuplicateCoupleId _ -> "duplicate couple id", "duplicate couple ids"
                    | DuplicateWilpId _ -> "duplicate wilp id", "duplicate wilp ids"
                    | DuplicateNameId _ -> "duplicate name id", "duplicate name ids"
                    | DuplicateNameText _ -> "duplicate name", "duplicate names"
                    | UnresolvedNameId _
                    | UnresolvedNameHolder _ -> "dropped name holding", "dropped name holdings"
                    | UnparseableNameDate _ -> "unparseable name date", "unparseable name dates"
                    | UnorderedNameHolding _ -> "unordered name", "unordered names"
                    | UnheldName _ -> "unheld name", "unheld names"
                    | WilpMissingPdeek _
                    | WilpMissingNameAndPdeek _
                    | UnknownPdeek _ -> "dropped wilp", "dropped huwilp"
                    | ConflictingWilpPdeek _ -> "conflicting-pdeek wilp", "conflicting-pdeek huwilp"

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
