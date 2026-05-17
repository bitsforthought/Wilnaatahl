namespace Wilnaatahl.Import

open System
open System.Text
open Wilnaatahl.Model
open Wilnaatahl.Import.JsonParser

/// Non-fatal warnings emitted during import. The transform records these as it
/// runs and surfaces them via `ImportResult.Warnings`; none of them prevent
/// the import from completing.
type ImportWarning =
    | UnresolvedCoupleId of personName: string * coupleId: int
    | UnresolvedMember of coupleId: int * memberId: int
    | UnresolvedWilpId of personName: string * wilpId: int
    | UnparseableDate of personName: string * fieldName: string * rawValue: string
    | UnparsableCoupleDate of coupleId: int * rawValue: string
    | DuplicatePersonId of id: int
    | DuplicateCoupleId of id: int
    | DuplicateWilpId of id: int
    | WilpMissingPdeek of id: int
    | WilpMissingNameAndPdeek of id: int
    | UnknownPdeek of wilpId: int * rawPdeek: string

/// Anything that prevents an import from completing.
type ImportError =
    | InvalidJson of string
    | EmptyPeopleArray

/// Typed people with their (optional) parent couple, the list of valid couples,
/// and any non-fatal warnings collected during validation.
type ImportResult = {
    PeopleAndCoupleIds: (Person * CoupleId option) list
    Couples: Couple list
    Warnings: ImportWarning list
}

module Transform =

    /// Tries to parse a string as a DateOnly using the tuple form of TryParse.
    let private tryParseDate (s: string) =
        match DateOnly.TryParse(s) with
        | true, d -> Some d
        | false, _ -> None

    /// Deduplicates records by a key, preserving input order. Returns the kept list
    /// and a warning per duplicate after the first occurrence.
    let private dedupBy keyOf warnOf items =
        let folder (seen, kept, warnings) item =
            let key = keyOf item

            if Set.contains key seen then
                seen, kept, warnOf key :: warnings
            else
                Set.add key seen, item :: kept, warnings

        let _, keptRev, warningsRev = List.fold folder (Set.empty, [], []) items
        List.rev keptRev, List.rev warningsRev

    /// Parses a `pdeek` string into the model's `Pdeek` DU, accepting the
    /// orthographic variants Sim Algyax spelling conventions allow:
    ///
    ///   - Case differences (`giskaast` vs `Giskaast`).
    ///   - Any Unicode whitespace inside or around the name (`Lax Gibuu`,
    ///     `Lax\u00A0Gibuu`, `LaxGibuu`).
    ///   - Apostrophes or glottal-stop markers of any variant — ASCII `'`,
    ///     curly `'`, modifier letter — appearing or absent (`Gisk'aast` vs
    ///     `Giskaast`).
    ///   - Underlined letters typical of Sim Algyax orthography, in either
    ///     precomposed form (e.g. `ḵ` U+1E35) or letter + combining diacritic
    ///     form (e.g. `k` + U+0331). The underline is dropped during matching
    ///     because the data may or may not carry it.
    ///
    /// Normalization: Unicode NFD-decompose → lower-case via invariant
    /// culture → keep only ASCII letters (a–z). For the canonical-form
    /// keywords below, this collapses every variant above to the same key.
    ///
    /// Recognized canonical forms:
    ///
    ///   - LaxGibuu: laxgibuu
    ///   - LaxSkiik: laxskiik, laxsgiik
    ///   - Ganeda:   ganeda, ganada, laxseel
    ///   - Giskaast: giskaast, giskahaast
    ///
    /// Returns `None` for unrecognized values.
    ///
    /// Invariant culture is the deliberate choice here: Sim Algyax has no
    /// .NET `CultureInfo`, so `ToLowerInvariant` is used to get deterministic,
    /// machine-independent ASCII case folding without claiming the data is in
    /// any particular locale.
    let private tryParsePdeek (raw: string) =
        let normalized =
            raw.Normalize(NormalizationForm.FormD).ToLowerInvariant()
            |> String.filter (fun c -> c >= 'a' && c <= 'z')

        match normalized with
        | "laxgibuu" -> Some LaxGibuu
        | "laxskiik"
        | "laxsgiik" -> Some LaxSkiik
        | "ganeda"
        | "ganada"
        | "laxseel" -> Some Ganeda
        | "giskaast"
        | "giskahaast" -> Some Giskaast
        | _ -> None

    /// Validates each `RawWilp` entry into a `Kinship` value. Entries with both
    /// name and pdeek become `Wilp { Name; Pdeek }`; entries with only pdeek
    /// become `UnknownWilp pdeek` (no warning — this is a first-class case). All
    /// other shapes are dropped with the corresponding warning: neither field
    /// (`WilpMissingNameAndPdeek`), name only (`WilpMissingPdeek`), or both
    /// fields present but pdeek unrecognized (`UnknownPdeek`). Returns a Map of
    /// usable Kinship values keyed by id plus the accumulated warnings.
    let private validateHuwilp (huwilp: RawWilp list) =
        let folder (huwilpById, warnings) (w: RawWilp) =
            match w.Name, w.Pdeek with
            | None, None -> huwilpById, WilpMissingNameAndPdeek w.Id :: warnings
            | Some _, None -> huwilpById, WilpMissingPdeek w.Id :: warnings
            | None, Some pdeekRaw ->
                match tryParsePdeek pdeekRaw with
                | None -> huwilpById, UnknownPdeek(w.Id, pdeekRaw) :: warnings
                | Some pdeek -> Map.add w.Id (UnknownWilp pdeek) huwilpById, warnings
            | Some name, Some pdeekRaw ->
                match tryParsePdeek pdeekRaw with
                | None -> huwilpById, UnknownPdeek(w.Id, pdeekRaw) :: warnings
                | Some pdeek ->
                    let kinship = Wilp { Name = WilpName name; Pdeek = pdeek }
                    Map.add w.Id kinship huwilpById, warnings

        let huwilpById, warningsRev = List.fold folder (Map.empty, []) huwilp
        huwilpById, List.rev warningsRev

    /// Validates that both members of each couple exist in the person set. Drops
    /// invalid couples and emits one UnresolvedMember warning per missing reference.
    let private validateCouples (personIds: Set<int>) (couples: RawCouple list) =
        let folder (kept, warnings) (c: RawCouple) =
            let missing = [
                if not (Set.contains c.Member1 personIds) then
                    c.Member1
                if not (Set.contains c.Member2 personIds) then
                    c.Member2
            ]

            match missing with
            | [] -> c :: kept, warnings
            | _ ->
                let memberWarnings = missing |> List.map (fun m -> UnresolvedMember(c.CoupleId, m))
                kept, memberWarnings @ warnings

        let keptRev, warningsRev = List.fold folder ([], []) couples
        List.rev keptRev, List.rev warningsRev

    /// For each person, resolves their `parents: int option` against the valid couple
    /// set. Unresolvable references become None and yield an UnresolvedCoupleId warning.
    let private resolveParents (validCoupleIds: Set<int>) (people: RawPerson list) =
        people
        |> List.map (fun p ->
            match p.Parents with
            | None -> None, []
            | Some cId ->
                if Set.contains cId validCoupleIds then
                    Some(CoupleId cId), []
                else
                    None, [ UnresolvedCoupleId(p.Name, cId) ])

    /// Resolves each person's `wilp: int option` against the validated huwilp map.
    /// A `None` reference yields `NoneProvided` silently; a reference that doesn't
    /// resolve yields `NoneProvided` plus an `UnresolvedWilpId` warning.
    let private resolveHuwilp (huwilpById: Map<int, Kinship>) (people: RawPerson list) =
        people
        |> List.map (fun p ->
            match p.Wilp with
            | None -> NoneProvided, []
            | Some wId ->
                match Map.tryFind wId huwilpById with
                | Some kinship -> kinship, []
                | None -> NoneProvided, [ UnresolvedWilpId(p.Name, wId) ])

    /// Parses an optional ISO-8601 normalized person date, warning if present but
    /// unparseable. Absent input yields None with no warning.
    let private parsePersonDate personName fieldName normalizedDate =
        match normalizedDate with
        | None -> None, []
        | Some s ->
            match tryParseDate s with
            | Some d -> Some d, []
            | None -> None, [ UnparseableDate(personName, fieldName, s) ]

    /// Parses an optional ISO-8601 couple dateOfUnion, warning if present but
    /// unparseable. Absent input yields None with no warning.
    let private parseCoupleDate coupleId rawDate =
        match rawDate with
        | None -> None, []
        | Some s ->
            match tryParseDate s with
            | Some d -> Some d, []
            | None -> None, [ UnparsableCoupleDate(coupleId, s) ]

    /// Transforms a decoded RawFile into typed model records.
    ///
    /// Steps: reject empty input; deduplicate people, couples, and huwilp by id;
    /// validate the huwilp table (warn-and-drop on any entry missing name and/or
    /// pdeek, or whose pdeek string is unknown); drop couples whose members don't
    /// resolve; resolve each person's parent-couple and wilp links; parse
    /// normalized dates; build Couple values via `Couple.create`.
    let internal transform (rawFile: RawFile) : Result<ImportResult, ImportError> =
        if List.isEmpty rawFile.People then
            Error EmptyPeopleArray
        else
            let people, dupPersonWarnings =
                dedupBy (fun (p: RawPerson) -> p.Id) DuplicatePersonId rawFile.People

            let couples, dupCoupleWarnings =
                dedupBy (fun (c: RawCouple) -> c.CoupleId) DuplicateCoupleId rawFile.Couples

            let huwilp, dupHuwilpWarnings =
                dedupBy (fun (w: RawWilp) -> w.Id) DuplicateWilpId rawFile.Huwilp

            let huwilpById, huwilpValidationWarnings = validateHuwilp huwilp

            let personIds = people |> List.map (fun p -> p.Id) |> Set.ofList
            let validCouples, memberWarnings = validateCouples personIds couples
            let validCoupleIds = validCouples |> List.map (fun c -> c.CoupleId) |> Set.ofList

            let parentResults = resolveParents validCoupleIds people
            let parentWarnings = parentResults |> List.collect snd
            let resolvedParents = parentResults |> List.map fst

            let huwilpResults = resolveHuwilp huwilpById people
            let huwilpWarnings = huwilpResults |> List.collect snd
            let resolvedHuwilp = huwilpResults |> List.map fst

            let peopleResults =
                List.zip3 people resolvedParents resolvedHuwilp
                |> List.map (fun (rawPerson, parents, kinship) ->
                    let dob, dobWarnings =
                        parsePersonDate rawPerson.Name "normalizedDateOfBirth" rawPerson.NormalizedDateOfBirth

                    let dod, dodWarnings =
                        parsePersonDate rawPerson.Name "normalizedDateOfDeath" rawPerson.NormalizedDateOfDeath

                    let person = {
                        Id = PersonId rawPerson.Id
                        Label = Some rawPerson.Name
                        Kinship = kinship
                        Shape = if rawPerson.Gender = "F" then Sphere else Cube
                        BirthOrder = rawPerson.BirthOrder |> Option.defaultValue 0
                        DateOfBirth = dob
                        DateOfDeath = dod
                    }

                    (person, parents), dobWarnings @ dodWarnings)

            let coupleResults =
                validCouples
                |> List.map (fun c ->
                    let dateOfUnion, warnings = parseCoupleDate c.CoupleId c.DateOfUnion

                    let couple =
                        Couple.create (CoupleId c.CoupleId) (PersonId c.Member1) (PersonId c.Member2) dateOfUnion

                    couple, warnings)

            let peopleAndCoupleIds = peopleResults |> List.map fst
            let dateWarnings = peopleResults |> List.collect snd
            let coupleList = coupleResults |> List.map fst
            let coupleDateWarnings = coupleResults |> List.collect snd

            let allWarnings =
                dupPersonWarnings
                @ dupCoupleWarnings
                @ dupHuwilpWarnings
                @ huwilpValidationWarnings
                @ memberWarnings
                @ parentWarnings
                @ huwilpWarnings
                @ dateWarnings
                @ coupleDateWarnings

            Ok {
                PeopleAndCoupleIds = peopleAndCoupleIds
                Couples = coupleList
                Warnings = allWarnings
            }

    /// Public entry point. Parses the JSON and transforms the result into typed
    /// model records, surfacing non-fatal warnings via `ImportResult.Warnings`.
    let fromJson (json: string) : Result<ImportResult, ImportError> =
        parseJson json |> Result.mapError InvalidJson |> Result.bind transform
