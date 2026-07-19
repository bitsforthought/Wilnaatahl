module Wilnaatahl.Tests.ViewModel.NodeContentTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Swensen.Unquote
open FSharp.Reflection
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel
open Wilnaatahl.Tests.TestData

// ---------------------------------------------------------------------------
// Shared fixtures
// ---------------------------------------------------------------------------

let private wilpA = { Name = WilpName "A"; Pdeek = Giskaast }
let private wilpB = { Name = WilpName "B"; Pdeek = Ganeda }

/// A held Name with no ordering metadata — the builders consume the caller's
/// order, so date/order are irrelevant when testing the builders directly.
let private held text = { Name = Name text; NameDate = None; NameOrder = None }

// The exact Gitxsan Pdeek display spellings produced by `Pdeek.displayName`, each
// carrying a combining macron-below (U+0331) — spelled with an escape so the source
// byte-encodes the same grapheme the runtime returns, regardless of editor
// normalization. Comments show the rendered grapheme.
let private giskaastDisplay = "Gisk\u0331'aast" // Gisḵ'aast
let private ganedaDisplay = "G\u0331aneda" // G̱aneda
let private laxGibuuDisplay = "Lax\u0331 Gibuu" // Lax̱ Gibuu

// ===========================================================================
// DisplayDate.ofDateAndText — the presentation-neutral date selection, tested
// directly so its precedence is pinned independently of the view-model builders.
// ===========================================================================

[<Fact>]
let ``DisplayDate ofDateAndText prefers the normalized date as a FormattedDate`` () =
    DisplayDate.ofDateAndText (Some(DateOnly(1925, 3, 10))) (Some "circa 1925")
    =! Some(FormattedDate(DateOnly(1925, 3, 10)))

[<Fact>]
let ``DisplayDate ofDateAndText falls back to the raw text as RawText`` () =
    DisplayDate.ofDateAndText None (Some "circa 1925") =! Some(RawText "circa 1925")

[<Fact>]
let ``DisplayDate ofDateAndText is None when neither date is present`` () =
    DisplayDate.ofDateAndText None None =! None

// ===========================================================================
// NodeLabel.build — asserts the whole NodeLabelView record so no field drifts.
// Presentation (date formatting, "(...)" parentheses, "\n" composition, "B"/"D"
// prefixes) now lives in the TS layer and is deliberately not asserted here.
//
// `build`'s final argument, `currentWilpDiffersFromRendered`, says whether the
// person's own Wilp differs from the Wilp they are being drawn in: true for an
// outside spouse drawn into their partner's Wilp, false for a member drawn in
// their own. Only the parenthesized Kinship line depends on it, so tests about
// the other fields pass false.
// ===========================================================================

// --- ColonialName / MostRecentName selection ---

[<Fact>]
let ``NodeLabel colonial name only`` () =
    let person = { Person.Empty with ColonialName = Some "Margaret Ashford" }

    NodeLabel.build person [] false
    =! { NodeLabelView.Empty with ColonialName = Some "Margaret Ashford" }

[<Fact>]
let ``NodeLabel most-recent Name is the head of the held Names`` () =
    let person = { Person.Empty with ColonialName = None }
    // Only the head is the most-recent Name; the rest never appear in the label.
    NodeLabel.build person [ held "Newest"; held "Middle"; held "Oldest" ] false
    =! { NodeLabelView.Empty with MostRecentName = Some "Newest" }

[<Fact>]
let ``NodeLabel carries both colonial name and most-recent Name`` () =
    let person = { Person.Empty with ColonialName = Some "Margaret Ashford" }

    NodeLabel.build person [ held "The Mayor" ] false
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "Margaret Ashford"
               MostRecentName = Some "The Mayor"
       }

[<Fact>]
let ``NodeLabel with no colonial name, no Names, no dates is empty`` () =
    NodeLabel.build Person.Empty [] false =! NodeLabelView.Empty

// --- The parenthesized Kinship line ---

/// The text of the parenthesized Kinship line for each shape of `Person.Kinship`,
/// with the person drawn outside their own Wilp so the line is shown at all.
let kinshipLineCases =
    TheoryData<Kinship, string option>(
        // A known Wilp shows its bare name.
        struct (Wilp wilpB, Some "B"),
        // No Wilp but a known Pdeek shows the Pdeek's Gitxsan display spelling.
        struct (UnknownWilp LaxGibuu, Some laxGibuuDisplay),
        // Neither: nothing to show, with or without a free-form kinship note.
        struct (NoneProvided None, None),
        struct (NoneProvided(Some "a note"), None)
    )

[<Theory>]
[<MemberData(nameof kinshipLineCases)>]
let ``NodeLabel Kinship line text follows the person's Kinship`` (kinship: Kinship) (expected: string option) =
    let person = { Person.Empty with ColonialName = Some "Spouse"; Kinship = kinship }

    NodeLabel.build person [] true
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "Spouse"
               KinshipParen = expected
       }

/// Names, Wilp names, and Pdeek display spellings are drawn from one small pool so
/// that a held Name coincides with the person's Wilp name — the case the Kinship
/// line's de-duplication rule turns on — often enough to be exercised.
let private labelTextGen = Gen.elements [ "A"; "B"; "The Mayor"; laxGibuuDisplay ]

/// Every `Pdeek`, derived reflectively so a new clan is covered without editing this
/// file. `Pdeek`'s cases are all nullary, so the default generator needs no tuning.
let private pdeekGen = ArbMap.defaults |> ArbMap.generate<Pdeek>

let private wilpGen =
    gen {
        let! name = labelTextGen
        let! pdeek = pdeekGen
        return { Name = WilpName name; Pdeek = pdeek }
    }

/// A `Wilp` Kinship — the only shape the de-duplication rule is specified for.
let private wilpKinshipGen = wilpGen |> Gen.map Wilp

/// Every shape of `Kinship`. Hand-written rather than derived, because the payloads
/// must come from `labelTextGen`'s small pool for names to collide.
let private anyKinshipGen =
    Gen.oneof [
        wilpKinshipGen
        pdeekGen |> Gen.map UnknownWilp
        labelTextGen |> Gen.optionOf |> Gen.map NoneProvided
    ]

/// `anyKinshipGen` enumerates `Kinship`'s cases by hand, so a newly added case would
/// silently never be generated and the properties below would stay green while
/// covering less. This fails when a case is added, sending the author to the generator.
[<Fact>]
let ``anyKinshipGen generates every Kinship case`` () =
    FSharpType.GetUnionCases typeof<Kinship> |> Array.map _.Name |> Set.ofArray
    =! set [ "Wilp"; "UnknownWilp"; "NoneProvided" ]

/// Dates anywhere in the first eight millennia; only their presence matters here.
let private dateGen = Gen.choose (1, 3000000) |> Gen.map DateOnly.FromDayNumber

/// A person and their held Names (most-recent-first), varying every field
/// `NodeLabel.build` reads. The Kinship generator is a parameter so a property that
/// only holds for one Kinship shape can generate that shape every time rather than
/// spending most iterations on vacuously-true cases.
let private personAndNamesGen kinshipGen =
    gen {
        let! colonialName = labelTextGen |> Gen.optionOf
        let! kinship = kinshipGen
        let! birthWilp = wilpGen |> Gen.optionOf
        let! dateOfBirth = dateGen |> Gen.optionOf
        let! dateOfDeath = dateGen |> Gen.optionOf
        let! dateOfBirthText = labelTextGen |> Gen.optionOf
        let! dateOfDeathText = labelTextGen |> Gen.optionOf
        let! namesHeld = labelTextGen |> Gen.map held |> Gen.listOf

        let person = {
            Person.Empty with
                ColonialName = colonialName
                Kinship = kinship
                BirthWilp = birthWilp
                DateOfBirth = dateOfBirth
                DateOfDeath = dateOfDeath
                DateOfBirthText = dateOfBirthText
                DateOfDeathText = dateOfDeathText
        }

        return person, namesHeld
    }

/// Pins both halves of `currentWilpDiffersFromRendered`'s contract at once: drawn in
/// the person's own Wilp there is never a Kinship line, and the argument changes no
/// other field.
[<Property>]
let ``NodeLabel drawn in the person's own Wilp differs only by dropping the Kinship line`` () =
    Prop.forAll (personAndNamesGen anyKinshipGen |> Arb.fromGen) (fun (person, namesHeld) ->
        let shown = NodeLabel.build person namesHeld true
        NodeLabel.build person namesHeld false = { shown with KinshipParen = None })

/// The de-duplication rule is specified for the bare Wilp name only — a Pdeek display
/// spelling is shown even in the (implausible) case that it coincides with a held Name
/// — so this generates only `Wilp` Kinships rather than asserting vacuously for the
/// other shapes.
[<Property>]
let ``NodeLabel Kinship line never repeats the most-recent Name for a known Wilp`` () =
    Prop.forAll (personAndNamesGen wilpKinshipGen |> Arb.fromGen) (fun (person, namesHeld) ->
        let label = NodeLabel.build person namesHeld true
        label.KinshipParen <> label.MostRecentName)

[<Fact>]
let ``NodeLabel Kinship line ignores BirthWilp entirely`` () =
    // The Kinship line is driven purely by Person.Kinship; a differing BirthWilp
    // must not leak into it (that is the overlay's concern).
    let adopted = {
        Person.Empty with
            ColonialName = Some "Adopted"
            Kinship = Wilp wilpA
            BirthWilp = Some wilpB
    }

    NodeLabel.build adopted [] true
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "Adopted"
               KinshipParen = Some "A"
       }

[<Fact>]
let ``NodeLabel omits the Wilp Kinship line when it repeats the most-recent Name`` () =
    // An outside spouse whose most-recent Name is the same text as their Wilp: the
    // parenthesized Wilp text would merely repeat the Name, so it is dropped.
    let person = {
        Person.Empty with
            ColonialName = Some "Spouse"
            Kinship = Wilp wilpB
    }

    // wilpB.Name is "B", and the most-recent Name is also "B".
    NodeLabel.build person [ held "B" ] true
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "Spouse"
               MostRecentName = Some "B"
       }

[<Fact>]
let ``NodeLabel keeps the Wilp Kinship line when the most-recent Name differs`` () =
    let person = {
        Person.Empty with
            ColonialName = Some "Spouse"
            Kinship = Wilp wilpB
    }

    NodeLabel.build person [ held "The Mayor" ] true
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "Spouse"
               MostRecentName = Some "The Mayor"
               KinshipParen = Some "B"
       }

[<Fact>]
let ``NodeLabel with only a Name equal to the Wilp keeps that Name and no Kinship line`` () =
    // No colonial name: the Name is the most-recent Name, and the redundant Wilp
    // text is still suppressed, so the Name is not repeated.
    let person = { Person.Empty with ColonialName = None; Kinship = Wilp wilpB }

    NodeLabel.build person [ held "B" ] true
    =! { NodeLabelView.Empty with MostRecentName = Some "B" }

[<Fact>]
let ``NodeLabel does not suppress the Wilp text when only the colonial name equals the Wilp`` () =
    // Suppression is driven solely by the most-recent held Name, never the colonial
    // name. With no held Names the Kinship text stays, even if the colonial name
    // happens to match the Wilp.
    let person = { Person.Empty with ColonialName = Some "B"; Kinship = Wilp wilpB }

    NodeLabel.build person [] true
    =! {
           NodeLabelView.Empty with
               ColonialName = Some "B"
               KinshipParen = Some "B"
       }

// --- Born / Died: the DisplayDate selection carried on the label ---

[<Fact>]
let ``NodeLabel carries both a formatted birth and death date`` () =
    let person = {
        Person.Empty with
            DateOfBirth = Some(DateOnly(1932, 4, 17))
            DateOfDeath = Some(DateOnly(2011, 3, 2))
    }

    NodeLabel.build person [] false
    =! {
           NodeLabelView.Empty with
               Born = Some(FormattedDate(DateOnly(1932, 4, 17)))
               Died = Some(FormattedDate(DateOnly(2011, 3, 2)))
       }

[<Fact>]
let ``NodeLabel carries a formatted birth date alone`` () =
    let person = { Person.Empty with DateOfBirth = Some(DateOnly(1932, 4, 17)) }

    NodeLabel.build person [] false
    =! {
           NodeLabelView.Empty with
               Born = Some(FormattedDate(DateOnly(1932, 4, 17)))
       }

[<Fact>]
let ``NodeLabel carries a formatted death date alone`` () =
    let person = { Person.Empty with DateOfDeath = Some(DateOnly(2011, 3, 2)) }

    NodeLabel.build person [] false
    =! {
           NodeLabelView.Empty with
               Died = Some(FormattedDate(DateOnly(2011, 3, 2)))
       }

[<Fact>]
let ``NodeLabel carries raw birth text verbatim as RawText`` () =
    let person = { Person.Empty with DateOfBirthText = Some "circa 1925" }

    NodeLabel.build person [] false
    =! { NodeLabelView.Empty with Born = Some(RawText "circa 1925") }

[<Fact>]
let ``NodeLabel prefers the normalized birth date over the raw text`` () =
    let person = {
        Person.Empty with
            DateOfBirth = Some(DateOnly(1925, 3, 10))
            DateOfBirthText = Some "circa 1925"
    }

    NodeLabel.build person [] false
    =! {
           NodeLabelView.Empty with
               Born = Some(FormattedDate(DateOnly(1925, 3, 10)))
       }

[<Fact>]
let ``NodeLabel mixes a normalized birth with a raw death`` () =
    let person = {
        Person.Empty with
            DateOfBirth = Some(DateOnly(1932, 4, 17))
            DateOfDeathText = Some "unknown"
    }

    NodeLabel.build person [] false
    =! {
           NodeLabelView.Empty with
               Born = Some(FormattedDate(DateOnly(1932, 4, 17)))
               Died = Some(RawText "unknown")
       }

// --- Full composition: every field present at once ---

[<Fact>]
let ``NodeLabel composes colonial, Name, Kinship, and both dates together`` () =
    let person = {
        Person.Empty with
            ColonialName = Some "Margaret Ashford"
            Kinship = Wilp wilpB
            DateOfBirth = Some(DateOnly(1932, 4, 17))
            DateOfDeath = Some(DateOnly(2011, 3, 2))
    }

    NodeLabel.build person [ held "The Mayor" ] true
    =! {
           ColonialName = Some "Margaret Ashford"
           MostRecentName = Some "The Mayor"
           KinshipParen = Some "B"
           Born = Some(FormattedDate(DateOnly(1932, 4, 17)))
           Died = Some(FormattedDate(DateOnly(2011, 3, 2)))
       }

// ===========================================================================
// NodeDetail.build
// ===========================================================================

// --- Title variants ---

[<Fact>]
let ``NodeDetail title is the colonial name when only it is present`` () =
    let person = { Person.Empty with ColonialName = Some "Margaret Ashford" }
    (NodeDetail.build person []).Title =! "Margaret Ashford"

[<Fact>]
let ``NodeDetail title is the most-recent Name when only Gitxsan Names are present`` () =
    let person = { Person.Empty with ColonialName = None }
    (NodeDetail.build person [ held "The Mayor"; held "Doc" ]).Title =! "The Mayor"

[<Fact>]
let ``NodeDetail title combines most-recent Name and colonial name`` () =
    let person = { Person.Empty with ColonialName = Some "Margaret Ashford" }

    (NodeDetail.build person [ held "The Mayor" ]).Title
    =! "The Mayor (Margaret Ashford)"

[<Fact>]
let ``NodeDetail title is empty when neither name is present`` () =
    (NodeDetail.build Person.Empty []).Title =! ""

// --- Kinship rows for each Kinship shape ---

[<Fact>]
let ``NodeDetail Kinship Wilp yields a current Wilp row and a current Pdeek row`` () =
    let person = { Person.Empty with Kinship = Wilp wilpA }

    (NodeDetail.build person []).Kinship
    =! [ CurrentWilp "A"; CurrentPdeek giskaastDisplay ]

[<Fact>]
let ``NodeDetail Kinship UnknownWilp yields only a current Pdeek row`` () =
    let person = { Person.Empty with Kinship = UnknownWilp LaxGibuu }
    (NodeDetail.build person []).Kinship =! [ CurrentPdeek laxGibuuDisplay ]

[<Fact>]
let ``NodeDetail Kinship NoneProvided None yields an unknown row`` () =
    let person = { Person.Empty with Kinship = NoneProvided None }
    (NodeDetail.build person []).Kinship =! [ KinshipUnknown ]

[<Fact>]
let ``NodeDetail Kinship NoneProvided with a note yields a note row`` () =
    let person = {
        Person.Empty with
            Kinship = NoneProvided(Some "adopted, clan unknown")
    }

    (NodeDetail.build person []).Kinship =! [ KinshipNote "adopted, clan unknown" ]

// --- Birth-Wilp (adoption) rows ---

[<Fact>]
let ``NodeDetail appends Birth Wilp rows when BirthWilp differs from the current Wilp`` () =
    let person = { Person.Empty with Kinship = Wilp wilpA; BirthWilp = Some wilpB }

    (NodeDetail.build person []).Kinship
    =! [
        CurrentWilp "A"
        CurrentPdeek giskaastDisplay
        BirthWilp "B"
        BirthPdeek ganedaDisplay
    ]

[<Fact>]
let ``NodeDetail omits Birth Wilp rows when BirthWilp is absent`` () =
    let person = { Person.Empty with Kinship = Wilp wilpA; BirthWilp = None }

    (NodeDetail.build person []).Kinship
    =! [ CurrentWilp "A"; CurrentPdeek giskaastDisplay ]

[<Fact>]
let ``NodeDetail omits Birth Wilp rows when BirthWilp equals the current Wilp by value`` () =
    // A freshly built record equal in value to the current Wilp (a distinct
    // instance) must still be recognized as equal — structural, not reference,
    // equality gates the birth rows.
    let person = {
        Person.Empty with
            Kinship = Wilp wilpA
            BirthWilp = Some { Name = WilpName "A"; Pdeek = Giskaast }
    }

    (NodeDetail.build person []).Kinship
    =! [ CurrentWilp "A"; CurrentPdeek giskaastDisplay ]

[<Fact>]
let ``NodeDetail shows Birth Wilp rows when only the Pdeek differs`` () =
    // Same Wilp name, different Pdeek: the Wilps are not structurally equal, so a
    // comparison that looked only at the name would wrongly omit the rows.
    let person = {
        Person.Empty with
            Kinship = Wilp wilpA
            BirthWilp = Some { Name = WilpName "A"; Pdeek = Ganeda }
    }

    (NodeDetail.build person []).Kinship
    =! [
        CurrentWilp "A"
        CurrentPdeek giskaastDisplay
        BirthWilp "A"
        BirthPdeek ganedaDisplay
    ]

[<Fact>]
let ``NodeDetail shows Birth Wilp rows when only the Wilp name differs`` () =
    let person = {
        Person.Empty with
            Kinship = Wilp wilpA
            BirthWilp = Some { Name = WilpName "C"; Pdeek = Giskaast }
    }

    (NodeDetail.build person []).Kinship
    =! [
        CurrentWilp "A"
        CurrentPdeek giskaastDisplay
        BirthWilp "C"
        BirthPdeek giskaastDisplay
    ]

[<Fact>]
let ``NodeDetail shows Birth Wilp rows when current Kinship is UnknownWilp`` () =
    let person = {
        Person.Empty with
            Kinship = UnknownWilp LaxGibuu
            BirthWilp = Some wilpB
    }

    (NodeDetail.build person []).Kinship
    =! [ CurrentPdeek laxGibuuDisplay; BirthWilp "B"; BirthPdeek ganedaDisplay ]

[<Fact>]
let ``NodeDetail shows Birth Wilp rows when current Kinship is NoneProvided`` () =
    let person = {
        Person.Empty with
            Kinship = NoneProvided None
            BirthWilp = Some wilpB
    }

    (NodeDetail.build person []).Kinship
    =! [ KinshipUnknown; BirthWilp "B"; BirthPdeek ganedaDisplay ]

// --- Dates: FormattedDate vs. RawText fallback ---

[<Fact>]
let ``NodeDetail Born and Died carry formatted dates`` () =
    let person = {
        Person.Empty with
            DateOfBirth = Some(DateOnly(1925, 3, 10))
            DateOfDeath = Some(DateOnly(1980, 7, 22))
    }

    let detail = NodeDetail.build person []
    detail.Born =! Some(FormattedDate(DateOnly(1925, 3, 10)))
    detail.Died =! Some(FormattedDate(DateOnly(1980, 7, 22)))

[<Fact>]
let ``NodeDetail Born falls back to raw text verbatim as RawText`` () =
    let person = { Person.Empty with DateOfBirthText = Some "circa 1925" }
    (NodeDetail.build person []).Born =! Some(RawText "circa 1925")

[<Fact>]
let ``NodeDetail Died falls back to raw text verbatim as RawText`` () =
    let person = { Person.Empty with DateOfDeathText = Some "unknown, before 1970" }
    (NodeDetail.build person []).Died =! Some(RawText "unknown, before 1970")

[<Fact>]
let ``NodeDetail prefers the normalized date over raw text`` () =
    let person = {
        Person.Empty with
            DateOfBirth = Some(DateOnly(1925, 3, 10))
            DateOfBirthText = Some "circa 1925"
    }

    (NodeDetail.build person []).Born =! Some(FormattedDate(DateOnly(1925, 3, 10)))

[<Fact>]
let ``NodeDetail Born and Died are None when the dates are absent`` () =
    let detail = NodeDetail.build Person.Empty []
    detail.Born =! None
    detail.Died =! None

// --- Other names held ---

[<Fact>]
let ``NodeDetail OtherNames excludes the most-recent Name and keeps order`` () =
    let person = { Person.Empty with ColonialName = None }

    (NodeDetail.build person [ held "The Mayor"; held "Lefty"; held "Doc" ]).OtherNames
    =! [ "Lefty"; "Doc" ]

[<Fact>]
let ``NodeDetail OtherNames is empty for a single held Name`` () =
    let person = { Person.Empty with ColonialName = Some "Margaret Ashford" }
    (NodeDetail.build person [ held "The Mayor" ]).OtherNames =! []

[<Fact>]
let ``NodeDetail OtherNames is empty for no held Names`` () =
    (NodeDetail.build Person.Empty []).OtherNames =! []

/// The builder consumes `namesHeldBy`'s most-recent-first ordering wholesale: the head becomes
/// the title and the tail keeps that same order as the other names held.
[<Fact>]
let ``NodeDetail consumes namesHeldBy so the most-recent Name titles the rest in order`` () =
    let holder = { Person.Empty with Id = PersonId 1; ColonialName = None }

    let oldest = { Name = Name "Oldest"; NameDate = on 1950 1 1; NameOrder = None }

    let middle = { Name = Name "Middle"; NameDate = on 1970 1 1; NameOrder = None }

    let newest = { Name = Name "Newest"; NameDate = on 1990 1 1; NameOrder = None }

    let graph =
        createFamilyGraph [ (holder, None) ] [] [ PersonId 1, oldest; PersonId 1, newest; PersonId 1, middle ]

    let detail = NodeDetail.build holder (graph |> namesHeldBy (PersonId 1))
    detail.Title =! "Newest"
    detail.OtherNames =! [ "Middle"; "Oldest" ]
