module Wilnaatahl.Tests.Persistence.TransformTests

open System
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Swensen.Unquote
open Wilnaatahl.Model
open Wilnaatahl.Persistence
open Wilnaatahl.Persistence.JsonContracts
open Wilnaatahl.Persistence.Transform
open Wilnaatahl.Tests.TestData

// ---------------------------------------------------------------------------
// Raw-input helpers
// ---------------------------------------------------------------------------

let private rawPerson id name gender = {
    Id = id
    Name = name
    Parents = None
    Wilp = None
    BirthOrder = None
    NormalizedDateOfBirth = None
    NormalizedDateOfDeath = None
    Gender = gender
}

let private withParents coupleId (p: RawPerson) = { p with Parents = Some coupleId }
let private withWilp wilpId (p: RawPerson) = { p with Wilp = Some wilpId }
let private withBirthOrder n (p: RawPerson) = { p with BirthOrder = Some n }
let private withNormDob d (p: RawPerson) = { p with NormalizedDateOfBirth = Some d }
let private withNormDod d (p: RawPerson) = { p with NormalizedDateOfDeath = Some d }

let private rawCouple id m1 m2 = { CoupleId = id; Member1 = m1; Member2 = m2; DateOfUnion = None }
let private withUnion d (c: RawCouple) = { c with DateOfUnion = Some d }

let private rawWilp id name pdeek = { Id = id; Name = Some name; Pdeek = Some pdeek }

let private rawFile people couples huwilp : RawFile = { People = people; Couples = couples; Huwilp = huwilp }

// ---------------------------------------------------------------------------
// Expected-output helpers and shared fixtures
// ---------------------------------------------------------------------------

let private personOf id name shape = {
    Person.Empty with
        Id = PersonId id
        Label = Some name
        Shape = shape
}

let private aliceRaw = rawPerson 0 "Alice" "F"
let private bobRaw = rawPerson 1 "Bob" "M"
let private carolRaw = rawPerson 2 "Carol" "F"

let private alice = personOf 0 "Alice" Sphere
let private bob = personOf 1 "Bob" Cube
let private carol = personOf 2 "Carol" Sphere

let private kinshipA = Wilp { Name = WilpName "A"; Pdeek = Giskaast }
let private kinshipB = Wilp { Name = WilpName "B"; Pdeek = LaxGibuu }

type DateField =
    | DateOfBirth
    | DateOfDeath

let private withDate field value =
    match field with
    | DateOfBirth -> withNormDob value
    | DateOfDeath -> withNormDod value

let private setDate field value (p: Person) =
    match field with
    | DateOfBirth -> { p with DateOfBirth = value }
    | DateOfDeath -> { p with DateOfDeath = value }

let private coupleIdForDateCase = 100

// ---------------------------------------------------------------------------
// MemberData sources for parameterized theories below. These are non-`private`
// `let` bindings so xUnit's reflection-based discovery can find them as public
// static members of the module's compiled static class.
// ---------------------------------------------------------------------------

let pdeekVariants =
    TheoryData<string, Pdeek>(
        // Canonical ASCII spellings with internal whitespace.
        struct ("Lax Gibuu", LaxGibuu),
        struct ("Lax Skiik", LaxSkiik),
        // Common alternate spellings of Ganeda.
        struct ("Lax Seel", Ganeda),
        struct ("Lax See'l", Ganeda),
        struct ("Ganada", Ganeda),
        // Alternate spellings of the other clans.
        struct ("Lax Sgiik", LaxSkiik),
        struct ("Gisk'aast", Giskaast),
        struct ("Gisk'ahaast", Giskaast),
        // Case-insensitive matching.
        struct ("gIsKaAsT", Giskaast),
        // Non-breaking space (U+00A0) — must be treated as whitespace alongside
        // ASCII space and tab.
        struct ("Lax\u00A0Gibuu", LaxGibuu),
        // U+1E35 LATIN SMALL LETTER K WITH LINE BELOW — Sim Algyax's underlined
        // k in precomposed form; must resolve identically to plain k.
        struct ("Gisḵaast", Giskaast),
        // x + U+0331 COMBINING MACRON BELOW — the form used in Sim Algyax for
        // letters without a precomposed underlined variant in Unicode.
        struct ("Lax\u0331Gibuu", LaxGibuu)
    )

let genderShapeCases =
    TheoryData<string, NodeShape>(
        struct ("F", Sphere),
        struct ("M", Cube),
        // Any gender other than "F" falls back to Cube — pins the silent
        // default branch in Transform.fs.
        struct ("X", Cube)
    )

let birthOrderCases =
    TheoryData<int option, int>(struct (None, 0), struct (Some 3, 3))

let personDateCases =
    TheoryData<DateField, string option, DateOnly option, ImportWarning list>(
        struct (DateOfBirth, Some "1985-06-15", Some(DateOnly(1985, 6, 15)), []),
        struct (DateOfBirth, Some "not-iso", None, [ UnparseableDate("A", "normalizedDateOfBirth", "not-iso") ]),
        struct (DateOfBirth, None, None, []),
        struct (DateOfDeath, Some "bad-iso", None, [ UnparseableDate("A", "normalizedDateOfDeath", "bad-iso") ])
    )

let coupleDateCases =
    TheoryData<string option, DateOnly option, ImportWarning list>(
        struct (Some "1955-06-15", Some(DateOnly(1955, 6, 15)), []),
        struct (Some "bad-date", None, [ UnparsableCoupleDate(coupleIdForDateCase, "bad-date") ]),
        struct (None, None, [])
    )

// ---------------------------------------------------------------------------
// Error conditions
// ---------------------------------------------------------------------------

[<Fact>]
let ``transform empty people list returns EmptyPeopleArray`` () =
    transform (rawFile [] [] []) =! Error EmptyPeopleArray

// ---------------------------------------------------------------------------
// Couple and parent resolution
// ---------------------------------------------------------------------------

[<Fact>]
let ``transform person with valid parent couple gets correct CoupleId`` () =
    let mumRaw = rawPerson 0 "Mum" "F"
    let dadRaw = rawPerson 1 "Dad" "M"
    let kidRaw = rawPerson 2 "Kid" "F" |> withParents 100
    let c = rawCouple 100 0 1

    let mum = personOf 0 "Mum" Sphere
    let dad = personOf 1 "Dad" Cube
    let kid = personOf 2 "Kid" Sphere

    transform (rawFile [ mumRaw; dadRaw; kidRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ mum, None; dad, None; kid, Some(CoupleId 100) ]
        Couples = [ Couple.create (CoupleId 100) (PersonId 0) (PersonId 1) None ]
        Warnings = []
    }

[<Fact>]
let ``transform childless couple appears in output with both members unlinked`` () =
    let c = rawCouple 99 0 1

    transform (rawFile [ aliceRaw; bobRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        Couples = [ Couple.create (CoupleId 99) (PersonId 0) (PersonId 1) None ]
        Warnings = []
    }

[<Fact>]
let ``transform unknown CoupleId on person emits UnresolvedCoupleId and person becomes root`` () =
    let kidRaw = rawPerson 0 "Kid" "F" |> withParents 999
    let kid = personOf 0 "Kid" Sphere

    transform (rawFile [ kidRaw ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (kid, None) ]
        Couples = []
        Warnings = [ UnresolvedCoupleId("Kid", 999) ]
    }

[<Fact>]
let ``transform couple with unknown member is dropped and its children become roots`` () =
    let kidRaw = rawPerson 1 "Kid" "F" |> withParents 100
    let c = rawCouple 100 0 99 // 99 doesn't exist
    let kid = personOf 1 "Kid" Sphere

    transform (rawFile [ aliceRaw; kidRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; kid, None ]
        Couples = []
        Warnings = [ UnresolvedMember(100, 99); UnresolvedCoupleId("Kid", 100) ]
    }

[<Fact>]
let ``transform couple with both members unknown emits two UnresolvedMember warnings`` () =
    let c = rawCouple 100 98 99 // both unknown

    transform (rawFile [ aliceRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ UnresolvedMember(100, 99); UnresolvedMember(100, 98) ]
    }

[<Fact>]
let ``transform duplicate person id keeps first occurrence and emits DuplicatePersonId`` () =
    let aliceFirst = rawPerson 0 "Alice" "F"
    let aliceDup = rawPerson 0 "AliceDup" "M"

    transform (rawFile [ aliceFirst; aliceDup ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ DuplicatePersonId 0 ]
    }

[<Fact>]
let ``transform duplicate couple id keeps first occurrence and emits DuplicateCoupleId`` () =
    let coupleFirst = rawCouple 50 0 1
    let coupleDup = rawCouple 50 0 2

    transform (rawFile [ aliceRaw; bobRaw; carolRaw ] [ coupleFirst; coupleDup ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None; carol, None ]
        Couples = [ Couple.create (CoupleId 50) (PersonId 0) (PersonId 1) None ]
        Warnings = [ DuplicateCoupleId 50 ]
    }

// ---------------------------------------------------------------------------
// Huwilp validation
// ---------------------------------------------------------------------------

[<Fact>]
let ``transform with empty huwilp list produces no warnings and every person Kinship is NoneProvided`` () =
    transform (rawFile [ aliceRaw; bobRaw ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform person with wilp reference to fully-specified huwilp gets that Wilp Kinship`` () =
    let aliceWithWilp = aliceRaw |> withWilp 7
    let w = rawWilp 7 "A" "Giskaast"
    let expectedAlice = { alice with Kinship = kinshipA }

    transform (rawFile [ aliceWithWilp ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform person without wilp reference gets Kinship NoneProvided without warning`` () =
    let w = rawWilp 7 "A" "Giskaast"

    transform (rawFile [ aliceRaw ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform person referencing missing wilp id emits UnresolvedWilpId and Kinship is NoneProvided`` () =
    let aliceWithMissingWilp = aliceRaw |> withWilp 999

    transform (rawFile [ aliceWithMissingWilp ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ UnresolvedWilpId("Alice", 999) ]
    }

[<Fact>]
let ``transform huwilp with only name emits WilpMissingPdeek and references go unresolved`` () =
    let aliceWithLonely = aliceRaw |> withWilp 1
    let lonely = { Id = 1; Name = Some "Lonely"; Pdeek = None }

    transform (rawFile [ aliceWithLonely ] [] [ lonely ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ WilpMissingPdeek 1; UnresolvedWilpId("Alice", 1) ]
    }

[<Fact>]
let ``transform huwilp with only pdeek resolves references as UnknownWilp without warning`` () =
    let aliceWithUnknown = aliceRaw |> withWilp 2
    let w = { Id = 2; Name = None; Pdeek = Some "Ganeda" }
    let expectedAlice = { alice with Kinship = UnknownWilp Ganeda }

    transform (rawFile [ aliceWithUnknown ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform huwilp with only unknown pdeek emits UnknownPdeek and references go unresolved`` () =
    let aliceWithBadPdeek = aliceRaw |> withWilp 2
    let w = { Id = 2; Name = None; Pdeek = Some "NotAClan" }

    transform (rawFile [ aliceWithBadPdeek ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ UnknownPdeek(2, "NotAClan"); UnresolvedWilpId("Alice", 2) ]
    }

[<Fact>]
let ``UnknownWilp kinship does not contribute to FamilyGraph's named-huwilp set`` () =
    // The exclusion is what lets pdeek-only people render as partners-from-outside
    // rather than as new roots.
    let aliceWithUnknown = aliceRaw |> withWilp 2
    let w = { Id = 2; Name = None; Pdeek = Some "Ganeda" }
    let expectedAlice = { alice with Kinship = UnknownWilp Ganeda }

    let importResult = {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = []
    }

    transform (rawFile [ aliceWithUnknown ] [] [ w ]) =! Ok importResult

    let graph =
        FamilyGraph.createFamilyGraph importResult.PeopleAndCoupleIds importResult.Couples

    FamilyGraph.huwilp graph =! Set.empty

[<Fact>]
let ``transform huwilp with neither name nor pdeek emits WilpMissingNameAndPdeek`` () =
    let aliceWithEmpty = aliceRaw |> withWilp 3
    let empty = { Id = 3; Name = None; Pdeek = None }

    transform (rawFile [ aliceWithEmpty ] [] [ empty ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ WilpMissingNameAndPdeek 3; UnresolvedWilpId("Alice", 3) ]
    }

[<Fact>]
let ``transform continues validating huwilp entries after one with missing fields`` () =
    let aliceWithLater = aliceRaw |> withWilp 4
    let dropped = { Id = 3; Name = None; Pdeek = None }
    let later = rawWilp 4 "Ganeda house" "Ganeda"

    let expectedAlice = {
        alice with
            Kinship = Wilp { Name = WilpName "Ganeda house"; Pdeek = Ganeda }
    }

    transform (rawFile [ aliceWithLater ] [] [ dropped; later ])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = [ WilpMissingNameAndPdeek 3 ]
    }

[<Fact>]
let ``transform huwilp with both name and unknown pdeek emits UnknownPdeek`` () =
    let aliceWithMystery = aliceRaw |> withWilp 4
    let w = { Id = 4; Name = Some "Mystery"; Pdeek = Some "NotAClan" }

    transform (rawFile [ aliceWithMystery ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        Couples = []
        Warnings = [ UnknownPdeek(4, "NotAClan"); UnresolvedWilpId("Alice", 4) ]
    }

[<Theory>]
[<MemberData(nameof pdeekVariants)>]
let ``transform recognizes raw pdeek string and resolves it to the Pdeek DU`` (rawPdeek: string) (expected: Pdeek) =
    let aliceWithWilp = aliceRaw |> withWilp 1
    let w = { Id = 1; Name = Some "House"; Pdeek = Some rawPdeek }

    let expectedAlice = {
        alice with
            Kinship = Wilp { Name = WilpName "House"; Pdeek = expected }
    }

    transform (rawFile [ aliceWithWilp ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform duplicate huwilp id keeps first occurrence and emits DuplicateWilpId`` () =
    let aliceWithDup = aliceRaw |> withWilp 5
    let first = { Id = 5; Name = Some "First"; Pdeek = Some "Giskaast" }
    let dup = { Id = 5; Name = Some "Second"; Pdeek = Some "Ganeda" }

    let expectedAlice = {
        alice with
            Kinship = Wilp { Name = WilpName "First"; Pdeek = Giskaast }
    }

    transform (rawFile [ aliceWithDup ] [] [ first; dup ])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedAlice, None) ]
        Couples = []
        Warnings = [ DuplicateWilpId 5 ]
    }

[<Fact>]
let ``transform multiple people referencing different huwilp each get their own Kinship`` () =
    let aliceWithA = aliceRaw |> withWilp 1
    let bobWithB = bobRaw |> withWilp 2
    let w1 = rawWilp 1 "A" "Giskaast"
    let w2 = rawWilp 2 "B" "Lax Gibuu"
    let expectedAlice = { alice with Kinship = kinshipA }
    let expectedBob = { bob with Kinship = kinshipB }

    transform (rawFile [ aliceWithA; bobWithB ] [] [ w1; w2 ])
    =! Ok {
        PeopleAndCoupleIds = [ expectedAlice, None; expectedBob, None ]
        Couples = []
        Warnings = []
    }

// ---------------------------------------------------------------------------
// Field mapping
// ---------------------------------------------------------------------------

[<Theory>]
[<MemberData(nameof genderShapeCases)>]
let ``transform gender maps to shape`` (gender: string) (expected: NodeShape) =
    transform (rawFile [ rawPerson 0 "A" gender ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (personOf 0 "A" expected, None) ]
        Couples = []
        Warnings = []
    }

[<Theory>]
[<MemberData(nameof personDateCases)>]
let ``transform person date field parses ISO or emits UnparseableDate``
    (field: DateField)
    (input: string option)
    (expected: DateOnly option)
    (expectedWarnings: ImportWarning list)
    =
    let p =
        match input with
        | None -> rawPerson 0 "A" "F"
        | Some s -> rawPerson 0 "A" "F" |> withDate field s

    let expectedPerson = personOf 0 "A" Sphere |> setDate field expected

    transform (rawFile [ p ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedPerson, None) ]
        Couples = []
        Warnings = expectedWarnings
    }

[<Theory>]
[<MemberData(nameof coupleDateCases)>]
let ``transform Couple dateOfUnion parses ISO or emits UnparsableCoupleDate``
    (input: string option)
    (expected: DateOnly option)
    (expectedWarnings: ImportWarning list)
    =
    let c =
        match input with
        | None -> rawCouple coupleIdForDateCase 0 1
        | Some s -> rawCouple coupleIdForDateCase 0 1 |> withUnion s

    let expectedCouple =
        Couple.create (CoupleId coupleIdForDateCase) (PersonId 0) (PersonId 1) expected

    transform (rawFile [ aliceRaw; bobRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        Couples = [ expectedCouple ]
        Warnings = expectedWarnings
    }

[<Theory>]
[<MemberData(nameof birthOrderCases)>]
let ``transform BirthOrder uses supplied value or defaults to zero`` (supplied: int option) (expected: int) =
    let p =
        match supplied with
        | None -> rawPerson 0 "A" "F"
        | Some n -> rawPerson 0 "A" "F" |> withBirthOrder n

    let expectedPerson = { personOf 0 "A" Sphere with BirthOrder = expected }

    transform (rawFile [ p ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedPerson, None) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform copies the JSON person id into PersonId`` () =
    transform (rawFile [ rawPerson 7 "A" "F"; rawPerson 42 "B" "M" ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ personOf 7 "A" Sphere, None; personOf 42 "B" Cube, None ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform copies the JSON coupleId into CoupleId`` () =
    let c = rawCouple 250 0 1

    transform (rawFile [ aliceRaw; bobRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        Couples = [ Couple.create (CoupleId 250) (PersonId 0) (PersonId 1) None ]
        Warnings = []
    }

// ---------------------------------------------------------------------------
// Integration: Transform.fromJson end-to-end
// ---------------------------------------------------------------------------

[<Fact>]
let ``fromJson valid JSON returns people, couples, warnings and resolves wilp refs`` () =
    let json =
        """{
            "people": [
                {"id":0,"name":"Mum","gender":"F","wilp":0},
                {"id":1,"name":"Dad","gender":"M"},
                {"id":2,"name":"Kid","gender":"F","parents":100,"wilp":0}
            ],
            "couples": [
                {"coupleId":100,"member1":0,"member2":1}
            ],
            "huwilp": [
                {"id":0,"name":"House","pdeek":"Giskaast"}
            ]
        }"""

    let house = Wilp { Name = WilpName "House"; Pdeek = Giskaast }
    let mum = { personOf 0 "Mum" Sphere with Kinship = house }
    let dad = personOf 1 "Dad" Cube
    let kid = { personOf 2 "Kid" Sphere with Kinship = house }

    fromJson json
    =! Ok {
        PeopleAndCoupleIds = [ mum, None; dad, None; kid, Some(CoupleId 100) ]
        Couples = [ Couple.create (CoupleId 100) (PersonId 0) (PersonId 1) None ]
        Warnings = []
    }

[<Fact>]
let ``fromJson invalid JSON returns InvalidJson error`` () =
    // `match` (rather than `=!`) lets us pin the InvalidJson case without
    // pinning the wrapped parser error string, which is implementation-defined.
    match fromJson "{{bad json" with
    | Error(InvalidJson _) -> ()
    | other -> failwithf "Expected InvalidJson but got %A" other

[<Fact>]
let ``fromJson empty people array returns EmptyPeopleArray error`` () =
    fromJson """{"people": []}""" =! Error EmptyPeopleArray

[<Fact>]
let ``fromJson surfaces WilpMissingNameAndPdeek as a warning, not an error`` () =
    let json =
        """{
            "people":[{"id":0,"name":"A","gender":"F","wilp":9}],
            "huwilp":[{"id":9,"name":null,"pdeek":null}]
        }"""

    fromJson json
    =! Ok {
        PeopleAndCoupleIds = [ (personOf 0 "A" Sphere, None) ]
        Couples = []
        Warnings = [ WilpMissingNameAndPdeek 9; UnresolvedWilpId("A", 9) ]
    }

// ---------------------------------------------------------------------------
// Property: pdeek orthography normalization
//
// The MemberData theory above pins a curated set of known spellings. This
// metamorphic property checks the normalizer's invariants directly: starting
// from each canonical key, any combination of case changes, interspersed
// non-letter noise (whitespace incl. NBSP, apostrophes/glottal markers,
// hyphens, digits), and writing k as the precomposed underlined k (U+1E35)
// must still resolve to the same Pdeek. All injected variations are dropped by
// the NFD-decompose → lower-invariant → keep-only-ASCII-letters pipeline, so
// the normalized key — and therefore the recognized Pdeek — is unchanged.
// ---------------------------------------------------------------------------

/// Canonical normalized keys (one per Pdeek) the normalizer recognizes.
let private pdeekCanonicalKeys = [
    "laxgibuu", LaxGibuu
    "laxskiik", LaxSkiik
    "ganeda", Ganeda
    "giskaast", Giskaast
]

/// Characters that the normalizer discards (none are ASCII letters after
/// NFD-decomposition), so interspersing them must not change recognition.
let private pdeekNoiseChars = [ ' '; '\t'; '\u00A0'; '\''; '\u2019'; '\u02BC'; '-'; '0'; '9' ]

let private pdeekNoiseRun =
    gen {
        let! count = Gen.choose (0, 2)
        let! chars = Gen.arrayOfLength count (Gen.elements pdeekNoiseChars)
        return String chars
    }

/// Renders one canonical letter as an equivalence-preserving variant: either
/// case, and for k also the precomposed underlined k that NFD-decomposes to k.
let private pdeekLetterVariant (c: char) =
    if c = 'k' then
        Gen.elements [ "k"; "K"; "\u1E35" ]
    else
        Gen.elements [ string c; string (Char.ToUpperInvariant c) ]

let private pdeekVariantOf (canonicalKey: string) =
    let rec build remaining acc =
        match remaining with
        | [] ->
            gen {
                let! trailing = pdeekNoiseRun
                return acc + trailing
            }
        | c :: rest ->
            gen {
                let! noise = pdeekNoiseRun
                let! letter = pdeekLetterVariant c
                return! build rest (acc + noise + letter)
            }

    build (Seq.toList canonicalKey) ""

let private pdeekVariantGen =
    gen {
        let! canonicalKey, expected = Gen.elements pdeekCanonicalKeys
        let! variant = pdeekVariantOf canonicalKey
        return variant, expected
    }

[<Property>]
let ``pdeek recognition is invariant to case, noise, and underlined-k spelling`` () =
    Prop.forAll (Arb.fromGen pdeekVariantGen) (fun (rawPdeek, expected) ->
        let aliceWithWilp = aliceRaw |> withWilp 1
        let w = { Id = 1; Name = Some "House"; Pdeek = Some rawPdeek }

        let expectedAlice = {
            alice with
                Kinship = Wilp { Name = WilpName "House"; Pdeek = expected }
        }

        transform (rawFile [ aliceWithWilp ] [] [ w ]) = Ok {
            PeopleAndCoupleIds = [ (expectedAlice, None) ]
            Couples = []
            Warnings = []
        })

// ---------------------------------------------------------------------------
// Transform.toJson: FamilyGraph → JSON
//
// toJson is the inverse of fromJson for any graph built from a clean import.
// The strongest test is the round trip: build a graph from model data, write
// it out, read it back, and confirm the people and couples are reconstructed
// with no warnings. The fixtures below are chosen to cover every Kinship case
// (known Wilp, Pdeek-only UnknownWilp, NoneProvided), childless couples, dates,
// and birth order. They all give every Person a Label, since the persistence
// format requires a name.
// ---------------------------------------------------------------------------

/// A Pdeek-only affiliation and a fully-known Wilp sharing that Pdeek, both with
/// a Label. TestData's UnknownWilp person has no Label, so it can't round-trip.
let private pdeekOnlyPerson = {
    Person.Empty with
        Id = PersonId 300
        Label = Some "PdeekOnly"
        Kinship = UnknownWilp LaxGibuu
}

let private knownWilpPerson = {
    Person.Empty with
        Id = PersonId 301
        Label = Some "Known"
        Kinship = Wilp { Name = WilpName "M"; Pdeek = LaxGibuu }
}

let toJsonRoundTripCases =
    TheoryData<(Person * CoupleId option) list, Couple list>(
        // Known huwilp (two distinct), NoneProvided partner, dates, birth order.
        struct (testPeopleAndParents, testCouples),
        // Multiple couples and grandchildren.
        struct (extendedFamily, extendedCouples),
        // A childless couple plus a NoneProvided partner.
        struct ([ childlessHead, None; childlessPartner, None ], [ childlessCouple ]),
        // UnknownWilp alongside a known Wilp of the same Pdeek.
        struct ([ pdeekOnlyPerson, None; knownWilpPerson, None ], List.empty<Couple>)
    )

[<Theory>]
[<MemberData(nameof toJsonRoundTripCases)>]
let ``toJson then fromJson reconstructs the original people and couples``
    (peopleAndParents: (Person * CoupleId option) list)
    (couples: Couple list)
    =
    let graph = FamilyGraph.createFamilyGraph peopleAndParents couples

    let byPersonId = List.sortBy (fun (p: Person, _) -> p.Id.AsInt)
    let byCoupleId = List.sortBy (fun (c: Couple) -> c.Id.AsInt)

    match Transform.toJson graph |> fromJson with
    | Ok result ->
        byPersonId result.PeopleAndCoupleIds =! byPersonId peopleAndParents
        byCoupleId result.Couples =! byCoupleId couples
        result.Warnings =! []
    | Error error -> failwithf "Round trip failed: %A" error

[<Fact>]
let ``toJson synthesizes one shared huwilp entry per distinct affiliation`` () =
    // p0, p2 and p4 share Wilp "H"; p3 is Wilp "L"; p1 is NoneProvided. The
    // three "H" members must collapse to a single huwilp entry, giving two
    // entries total — the dedup the round-trip alone doesn't directly assert.
    let graph = FamilyGraph.createFamilyGraph testPeopleAndParents testCouples

    match Transform.toJson graph |> JsonReader.read with
    | Ok raw -> raw.Huwilp |> List.length =! 2
    | Error error -> failwithf "toJson produced unreadable JSON: %A" error
