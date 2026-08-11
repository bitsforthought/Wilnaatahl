namespace Wilnaatahl.Persistence

open System
open System.Globalization
open System.Text
open Wilnaatahl.Model
open Wilnaatahl.Persistence.JsonContracts
open Wilnaatahl.Persistence.JsonReader
open Wilnaatahl.Persistence.JsonWriter

/// Non-fatal warnings emitted during import. The transform records these as it
/// runs and surfaces them via `ImportResult.Warnings`; none of them prevent
/// the import from completing.
type ImportWarning =
    | UnresolvedCoupleId of personName: string * coupleId: int
    | UnresolvedMember of coupleId: int * memberId: int
    | SelfCoupledMember of coupleId: int * memberId: int
    | UnresolvedWilpId of personName: string * wilpId: int
    | UnresolvedBirthWilpId of personName: string * wilpId: int
    | BirthWilpNotNamed of personName: string * wilpId: int
    | IgnoredKinshipNote of personName: string
    | UnparseableDate of personName: string * fieldName: string * rawValue: string
    | UnparsableCoupleDate of coupleId: int * rawValue: string
    | DuplicatePersonId of id: int
    | DuplicateCoupleId of id: int
    | DuplicateWilpId of id: int
    | DuplicateNameId of id: int
    | DuplicateNameText of text: string
    | UnresolvedNameId of personId: int * nameId: int
    | UnresolvedNameHolder of nameId: int * personId: int
    | UnparseableNameDate of nameId: int * personId: int * rawValue: string
    | UnorderedNameHolding of nameId: int * personId: int
    | UnheldName of nameId: int * text: string
    | WilpMissingPdeek of id: int
    | WilpMissingNameAndPdeek of id: int
    | UnknownPdeek of wilpId: int * rawPdeek: string
    | ConflictingWilpPdeek of wilpId: int * wilpName: string * pdeek: Pdeek

/// Anything that prevents an import from completing.
type ImportError =
    | InvalidJson of string
    | EmptyPeopleArray

/// Typed people with their (optional) parent couple, the list of valid couples,
/// the resolved Name holdings, and any non-fatal warnings collected during
/// validation.
type ImportResult = {
    PeopleAndCoupleIds: (Person * CoupleId option) list
    Couples: Couple list
    NameHoldings: (PersonId * NameHeld) list
    Warnings: ImportWarning list
}

module Transform =

    /// A person's display name for warning messages: the colonial name when
    /// recorded, otherwise a fallback naming the JSON id (a person may have no
    /// colonial name, only Gitxsan Names).
    let private displayName (person: RawPerson) =
        person.Name |> Option.defaultValue $"#%d{person.Id}"

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
    /// fields present but pdeek unrecognized (`UnknownPdeek`).
    ///
    /// After per-entry validation, any Wilp name that resolves to more than one
    /// distinct Pdeek across the surviving named entries is a conflict: every
    /// entry sharing that name is dropped (there is no basis to prefer one
    /// Pdeek), each with a `ConflictingWilpPdeek` warning. Entries agreeing on a
    /// single Pdeek are consistent and kept; pdeek-only (`UnknownWilp`) entries
    /// have no name and never participate. Returns a Map of usable Kinship
    /// values keyed by id plus the accumulated warnings.
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

        // Named entries in input order, paired with the resolved Wilp so a name
        // can be grouped against every Pdeek it carries.
        let namedEntries =
            huwilp
            |> List.choose (fun w ->
                match Map.tryFind w.Id huwilpById with
                | Some(Wilp wilp) -> Some(w.Id, wilp)
                | _ -> None)

        let conflictingNames =
            namedEntries
            |> List.groupBy (fun (_, wilp) -> wilp.Name)
            |> List.choose (fun (name, entries) ->
                let distinctPdeek =
                    entries |> List.map (fun (_, wilp) -> wilp.Pdeek) |> List.distinct

                if List.length distinctPdeek > 1 then Some name else None)
            |> Set.ofList

        let conflictingEntries =
            namedEntries
            |> List.filter (fun (_, wilp) -> Set.contains wilp.Name conflictingNames)

        let conflictIds = conflictingEntries |> List.map fst |> Set.ofList

        let conflictWarnings =
            conflictingEntries
            |> List.map (fun (id, wilp) -> ConflictingWilpPdeek(id, wilp.Name.AsString, wilp.Pdeek))

        let usableHuwilpById =
            huwilpById |> Map.filter (fun id _ -> not (Set.contains id conflictIds))

        usableHuwilpById, List.rev warningsRev @ conflictWarnings

    /// Validates each couple's members. Drops a couple whose two members are the
    /// same person (`SelfCoupledMember`) — `Couple.create` rejects an equal pair —
    /// and drops one whose member doesn't exist in the person set, emitting one
    /// `UnresolvedMember` warning per missing reference. A dropped couple's id
    /// resolves to absent, so persons naming it as their parents become roots.
    let private validateCouples (personIds: Set<int>) (couples: RawCouple list) =
        let folder (kept, warnings) (c: RawCouple) =
            if c.Member1 = c.Member2 then
                kept, SelfCoupledMember(c.CoupleId, c.Member1) :: warnings
            else
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
                    None, [ UnresolvedCoupleId(displayName p, cId) ])

    /// Resolves each person's `wilp: int option` against the validated huwilp map,
    /// applying the `kinshipNote`. A `None` reference yields
    /// `NoneProvided kinshipNote` silently; a reference that doesn't resolve yields
    /// `NoneProvided kinshipNote` plus an `UnresolvedWilpId` warning. A note present
    /// alongside a resolving reference cannot be represented on the resulting
    /// Kinship, so it is dropped with an `IgnoredKinshipNote` warning.
    let private resolveHuwilp (huwilpById: Map<int, Kinship>) (people: RawPerson list) =
        people
        |> List.map (fun p ->
            match p.Wilp with
            | None -> NoneProvided p.KinshipNote, []
            | Some wId ->
                match Map.tryFind wId huwilpById with
                | Some kinship ->
                    let ignoredNoteWarnings =
                        match p.KinshipNote with
                        | Some _ -> [ IgnoredKinshipNote(displayName p) ]
                        | None -> []

                    kinship, ignoredNoteWarnings
                | None -> NoneProvided p.KinshipNote, [ UnresolvedWilpId(displayName p, wId) ])

    /// Resolves each person's raw `birthWilp` reference against the validated
    /// huwilp map into their birth Wilp. Only a *named* Wilp qualifies: an absent
    /// reference yields no Wilp; a reference resolving to a pdeek-only entry is
    /// dropped with `BirthWilpNotNamed`; an unresolvable reference is dropped with
    /// `UnresolvedBirthWilpId`.
    let private resolveBirthWilp (huwilpById: Map<int, Kinship>) (people: RawPerson list) =
        people
        |> List.map (fun p ->
            match p.BirthWilp with
            | None -> None, []
            | Some wId ->
                match Map.tryFind wId huwilpById with
                | Some(Wilp w) -> Some w, []
                | Some _ -> None, [ BirthWilpNotNamed(displayName p, wId) ]
                | None -> None, [ UnresolvedBirthWilpId(displayName p, wId) ])

    /// Deduplicates `names` by id (`DuplicateNameId`, keeping the first) into an
    /// id → Name table; distinct ids sharing text resolve to the same Name but
    /// flag the redundant entry (`DuplicateNameText`). Each `namesHeld` row is
    /// resolved against that table and the person set into a `(PersonId, NameHeld)`
    /// holding, dropping rows whose name (`UnresolvedNameId`) or holder
    /// (`UnresolvedNameHolder`) is unknown. A present-but-unparseable `nameDate`
    /// becomes `None` and is always flagged (`UnparseableNameDate`); a holding left
    /// with neither a `NameOrder` nor a `NameDate` is kept but flagged
    /// (`UnorderedNameHolding`), since it has no well-defined recency order. A
    /// deduplicated name referenced by no surviving holding is dropped as unheld
    /// (`UnheldName`). Returns the resolved holdings and the collected warnings.
    let private resolveNames (personIds: Set<int>) (names: RawName list) (namesHeld: RawNameHeld list) =
        let dedupedNames, dupNameIdWarnings =
            dedupBy (fun (n: RawName) -> n.Id) DuplicateNameId names

        let textFolder (n: RawName) (seenTexts, nameById, warnings) =
            let nameById = nameById |> Map.add n.Id (Name n.Text)

            if seenTexts |> Set.contains n.Text then
                seenTexts, nameById, DuplicateNameText n.Text :: warnings
            else
                seenTexts |> Set.add n.Text, nameById, warnings

        let _, nameById, dupNameTextWarnings =
            List.foldBack textFolder dedupedNames (Set.empty, Map.empty, [])

        let holdingFolder (h: RawNameHeld) (holdings, referenced, warnings) =
            match nameById |> Map.tryFind h.NameId with
            | None -> holdings, referenced, UnresolvedNameId(h.PersonId, h.NameId) :: warnings
            | Some name ->
                if personIds |> Set.contains h.PersonId then
                    // Parse the date at the boundary: a present-but-unparseable date
                    // becomes None and is always flagged, so the model never carries
                    // a raw date string.
                    let parsedDate, dateWarnings =
                        match h.NameDate with
                        | None -> None, []
                        | Some raw ->
                            match tryParseDate raw with
                            | Some date -> Some date, []
                            | None -> None, [ UnparseableNameDate(h.NameId, h.PersonId, raw) ]

                    let held = { Name = name; NameDate = parsedDate; NameOrder = h.NameOrder }

                    // A holding left with neither a NameOrder nor a NameDate has no
                    // well-defined recency order and will be sorted alphabetically; flag
                    // it but keep it.
                    let unorderedWarnings =
                        match h.NameOrder, parsedDate with
                        | None, None -> [ UnorderedNameHolding(h.NameId, h.PersonId) ]
                        | _ -> []

                    (PersonId h.PersonId, held) :: holdings,
                    referenced |> Set.add h.NameId,
                    dateWarnings @ unorderedWarnings @ warnings
                else
                    holdings, referenced, UnresolvedNameHolder(h.NameId, h.PersonId) :: warnings

        let holdings, referencedNameIds, holdingWarnings =
            List.foldBack holdingFolder namesHeld ([], Set.empty, [])

        let unheldNameWarnings =
            dedupedNames
            |> List.filter (fun n -> referencedNameIds |> Set.contains n.Id |> not)
            |> List.map (fun n -> UnheldName(n.Id, n.Text))

        let warnings =
            dupNameIdWarnings @ dupNameTextWarnings @ holdingWarnings @ unheldNameWarnings

        holdings, warnings

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
    /// Steps: reject empty input; deduplicate people, couples, huwilp, and names
    /// by id; validate the huwilp table (warn-and-drop on any entry missing name
    /// and/or pdeek, whose pdeek string is unknown, or sharing a name with a
    /// conflicting pdeek); drop couples whose members
    /// don't resolve; resolve each person's parent-couple, wilp, and birth-wilp
    /// links and their kinship note; resolve name holdings and drop unheld names;
    /// parse normalized dates; build Couple values via `Couple.create`.
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

            let birthWilpResults = resolveBirthWilp huwilpById people
            let birthWilpWarnings = birthWilpResults |> List.collect snd
            let resolvedBirthWilp = birthWilpResults |> List.map fst

            let nameHoldings, nameWarnings =
                resolveNames personIds rawFile.Names rawFile.NamesHeld

            let peopleResults =
                List.zip3 people resolvedParents (List.zip resolvedHuwilp resolvedBirthWilp)
                |> List.map (fun (rawPerson, parents, (kinship, birthWilp)) ->
                    let dob, dobWarnings =
                        parsePersonDate (displayName rawPerson) "normalizedDateOfBirth" rawPerson.NormalizedDateOfBirth

                    let dod, dodWarnings =
                        parsePersonDate (displayName rawPerson) "normalizedDateOfDeath" rawPerson.NormalizedDateOfDeath

                    let person = {
                        Id = PersonId rawPerson.Id
                        ColonialName = rawPerson.Name
                        Kinship = kinship
                        BirthWilp = birthWilp
                        Shape = if rawPerson.Gender = "F" then Sphere else Cube
                        BirthOrder = rawPerson.BirthOrder |> Option.defaultValue 0
                        DateOfBirth = dob
                        DateOfDeath = dod
                        DateOfBirthText = rawPerson.RawDateOfBirth
                        DateOfDeathText = rawPerson.RawDateOfDeath
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
                @ birthWilpWarnings
                @ nameWarnings
                @ dateWarnings
                @ coupleDateWarnings

            Ok {
                PeopleAndCoupleIds = peopleAndCoupleIds
                Couples = coupleList
                NameHoldings = nameHoldings
                Warnings = allWarnings
            }

    /// Public entry point. Parses the JSON and transforms the result into typed
    /// model records, surfacing non-fatal warnings via `ImportResult.Warnings`.
    let fromJson (json: string) : Result<ImportResult, ImportError> =
        read json |> Result.mapError InvalidJson |> Result.bind transform

    /// The canonical pdeek spelling for each Pdeek — the ASCII form
    /// `tryParsePdeek` recognizes — used when writing huwilp entries out.
    let private pdeekToRaw pdeek =
        match pdeek with
        | LaxGibuu -> "LaxGibuu"
        | LaxSkiik -> "LaxSkiik"
        | Ganeda -> "Ganeda"
        | Giskaast -> "Giskaast"

    /// Formats a date as the ISO-8601 yyyy-MM-dd string the reader parses back
    /// via DateOnly.TryParse. "O" is the round-trip standard specifier (the ISO
    /// date format); InvariantCulture is required because the Fable compiler
    /// rejects DateOnly.ToString without an explicit culture, and the culture is
    /// otherwise irrelevant to the "O" output.
    let private formatDate (date: DateOnly) =
        date.ToString("O", CultureInfo.InvariantCulture)

    /// Serializes a FamilyGraph to the JSON persistence format — the inverse of
    /// `fromJson` for any graph built from a clean import (re-reading the output
    /// reproduces the same people, couples, and Name holdings with no warnings).
    ///
    /// The graph carries no Wilp or Name ids (those are an import-format
    /// artifact), so this synthesizes them: one `huwilp` entry per distinct
    /// affiliation — spanning the union of the current Kinship and birth Wilps —
    /// and one `names` entry per distinct held-Name text, sharing an id among all
    /// people of the same Wilp or the same Name. Storage is held-only, so a Name
    /// that nobody holds is not represented and does not survive a round trip. A
    /// person's optional colonial name and raw dates are omitted when absent.
    let toJson (graph: FamilyGraph.FamilyGraph) : string =
        let people =
            FamilyGraph.allPeople graph |> Seq.sortBy (fun p -> p.Id.AsInt) |> Seq.toList

        let couples =
            FamilyGraph.couples graph |> Seq.sortBy (fun c -> c.Id.AsInt) |> Seq.toList

        // Invert the couple→children relation so each child knows its parent couple.
        let parentCoupleIdByPersonId =
            couples
            |> List.collect (fun couple ->
                FamilyGraph.findChildrenOfCouple couple graph
                |> List.map (fun childId -> childId.AsInt, couple.Id.AsInt))
            |> Map.ofList

        // Each distinct affiliation becomes one huwilp entry, keyed by its
        // Kinship so people who share an affiliation share an id. A birth Wilp is
        // a named Wilp, so it maps to the `Wilp w` Kinship case, letting a birth
        // Wilp reuse the current-Kinship entry when they coincide. The id is each
        // entry's index, so the list is already in id order.
        let kinshipsWithIds =
            people
            |> List.collect (fun p ->
                let fromKinship =
                    match p.Kinship with
                    | NoneProvided _ -> []
                    | known -> [ known ]

                let fromBirthWilp = p.BirthWilp |> Option.map Wilp |> Option.toList
                fromKinship @ fromBirthWilp)
            |> List.distinct
            |> List.mapi (fun id kinship -> kinship, id)

        let huwilpIdByKinship = Map.ofList kinshipsWithIds

        let rawHuwilp =
            kinshipsWithIds
            |> List.map (fun (kinship, id) ->
                match kinship with
                | Wilp w -> {
                    Id = id
                    Name = Some w.Name.AsString
                    Pdeek = Some(pdeekToRaw w.Pdeek)
                  }
                | UnknownWilp pdeek -> { Id = id; Name = None; Pdeek = Some(pdeekToRaw pdeek) }
                | NoneProvided _ -> failwith "NoneProvided was excluded from the huwilp id map above.")

        // Each distinct held-Name text becomes one `names` entry; a Name handed
        // down to several people collapses to a single entry they all reference.
        let holdings = graph |> FamilyGraph.allNameHoldings |> Seq.toList

        let nameTextsWithIds =
            holdings
            |> List.map (fun (_, held) -> held.Name.AsString)
            |> List.distinct
            |> List.mapi (fun id text -> text, id)

        let nameIdByText = Map.ofList nameTextsWithIds

        let rawNames =
            nameTextsWithIds |> List.map (fun (text, id) -> { Id = id; Text = text })

        let rawNamesHeld =
            holdings
            |> List.map (fun (holderId, held) -> {
                NameId = nameIdByText |> Map.find held.Name.AsString
                PersonId = holderId.AsInt
                NameDate = held.NameDate |> Option.map formatDate
                NameOrder = held.NameOrder
            })

        let rawPeople =
            people
            |> List.map (fun p -> {
                Id = p.Id.AsInt
                Name = p.ColonialName
                Parents = Map.tryFind p.Id.AsInt parentCoupleIdByPersonId
                Wilp = Map.tryFind p.Kinship huwilpIdByKinship
                BirthWilp = p.BirthWilp |> Option.bind (fun w -> Map.tryFind (Wilp w) huwilpIdByKinship)
                KinshipNote =
                    match p.Kinship with
                    | NoneProvided note -> note
                    | _ -> None
                BirthOrder = Some p.BirthOrder
                RawDateOfBirth = p.DateOfBirthText
                RawDateOfDeath = p.DateOfDeathText
                NormalizedDateOfBirth = p.DateOfBirth |> Option.map formatDate
                NormalizedDateOfDeath = p.DateOfDeath |> Option.map formatDate
                Gender =
                    match p.Shape with
                    | Sphere -> "F"
                    | Cube -> "M"
            })

        let rawCouples =
            couples
            |> List.map (fun couple ->
                let member1, member2 = couple.Members

                {
                    CoupleId = couple.Id.AsInt
                    Member1 = member1.AsInt
                    Member2 = member2.AsInt
                    DateOfUnion = couple.DateOfUnion |> Option.map formatDate
                })

        write {
            People = rawPeople
            Couples = rawCouples
            Huwilp = rawHuwilp
            Names = rawNames
            NamesHeld = rawNamesHeld
        }
