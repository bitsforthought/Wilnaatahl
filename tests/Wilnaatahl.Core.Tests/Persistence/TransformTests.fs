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

// MAINTENANCE NOTE: prefer asserting directly on the result of `transform` /
// `Transform.toJson` (and round-trip calls) rather than binding an intermediate
// and re-deriving expected values — this matches the majority style in this file.
// For expected RawPerson values use the shared `defaultRawPerson` (writer output)
// or `emptyRawPerson` (decoded input) baselines from TestData and override only
// the fields under test; for `NameHeld` dates use the shared `on` helper.

// ---------------------------------------------------------------------------
// Raw-input helpers
// ---------------------------------------------------------------------------

let private rawPerson id name gender = { emptyRawPerson with Id = id; Name = Some name; Gender = gender }

let private withParents coupleId (p: RawPerson) = { p with Parents = Some coupleId }
let private withWilp wilpId (p: RawPerson) = { p with Wilp = Some wilpId }
let private withBirthWilp wilpId (p: RawPerson) = { p with BirthWilp = Some wilpId }
let private withKinshipNote note (p: RawPerson) = { p with KinshipNote = Some note }
let private withoutName (p: RawPerson) = { p with Name = None }
let private withRawDob d (p: RawPerson) = { p with RawDateOfBirth = Some d }
let private withRawDod d (p: RawPerson) = { p with RawDateOfDeath = Some d }
let private withNormDob d (p: RawPerson) = { p with NormalizedDateOfBirth = Some d }
let private withNormDod d (p: RawPerson) = { p with NormalizedDateOfDeath = Some d }

let private rawCouple id m1 m2 = { CoupleId = id; Member1 = m1; Member2 = m2; DateOfUnion = None }
let private withUnion d (c: RawCouple) = { c with DateOfUnion = Some d }

let private rawWilp id name pdeek = { Id = id; Name = Some name; Pdeek = Some pdeek }

let private rawFile people couples huwilp : RawFile = {
    People = people
    Couples = couples
    Huwilp = huwilp
    Names = []
    NamesHeld = []
}

let private rawFileWithNames people couples huwilp names namesHeld : RawFile = {
    People = people
    Couples = couples
    Huwilp = huwilp
    Names = names
    NamesHeld = namesHeld
}

let private rawName id text : RawName = { Id = id; Text = text }

let private rawNameHeld nameId personId : RawNameHeld = {
    NameId = nameId
    PersonId = personId
    NameDate = None
    NameOrder = Some 1
}

// ---------------------------------------------------------------------------
// Expected-output helpers and shared fixtures
// ---------------------------------------------------------------------------

let private personOf id name shape = {
    Person.Empty with
        Id = PersonId id
        ColonialName = Some name
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
        NameHoldings = []
        Couples = [ Couple.create (CoupleId 100) (PersonId 0) (PersonId 1) None ]
        Warnings = []
    }

[<Fact>]
let ``transform childless couple appears in output with both members unlinked`` () =
    let c = rawCouple 99 0 1

    transform (rawFile [ aliceRaw; bobRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedMember(100, 99); UnresolvedCoupleId("Kid", 100) ]
    }

[<Fact>]
let ``transform couple with both members unknown emits two UnresolvedMember warnings`` () =
    let c = rawCouple 100 98 99 // both unknown

    transform (rawFile [ aliceRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedMember(100, 99); UnresolvedMember(100, 98) ]
    }

[<Fact>]
let ``transform couple whose two members are the same person is dropped with SelfCoupledMember`` () =
    // Both members reference the valid person 0, but `Couple.create` rejects an
    // equal pair, so the couple must be dropped before it reaches the constructor
    // rather than crashing the Result-based import. A child referencing the dropped
    // couple becomes a root (UnresolvedCoupleId), as for any other dropped couple.
    let c = rawCouple 100 0 0
    let kidRaw = rawPerson 1 "Kid" "F" |> withParents 100
    let kid = personOf 1 "Kid" Sphere

    transform (rawFile [ aliceRaw; kidRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; kid, None ]
        NameHoldings = []
        Couples = []
        Warnings = [ SelfCoupledMember(100, 0); UnresolvedCoupleId("Kid", 100) ]
    }

[<Fact>]
let ``transform self-couple whose shared member is also unknown emits only SelfCoupledMember`` () =
    // The two members are equal AND that person does not exist. The equal-pair
    // check runs before the existence check, so the couple is dropped with a
    // single SelfCoupledMember warning rather than an UnresolvedMember.
    let c = rawCouple 100 99 99

    transform (rawFile [ aliceRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ SelfCoupledMember(100, 99) ]
    }

[<Fact>]
let ``transform duplicate person id keeps first occurrence and emits DuplicatePersonId`` () =
    let aliceFirst = rawPerson 0 "Alice" "F"
    let aliceDup = rawPerson 0 "AliceDup" "M"

    transform (rawFile [ aliceFirst; aliceDup ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform person without wilp reference gets Kinship NoneProvided without warning`` () =
    let w = rawWilp 7 "A" "Giskaast"

    transform (rawFile [ aliceRaw ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform person referencing missing wilp id emits UnresolvedWilpId and Kinship is NoneProvided`` () =
    let aliceWithMissingWilp = aliceRaw |> withWilp 999

    transform (rawFile [ aliceWithMissingWilp ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
        Couples = []
        Warnings = []
    }

    transform (rawFile [ aliceWithUnknown ] [] [ w ]) =! Ok importResult

    let graph =
        FamilyGraph.createFamilyGraph importResult.PeopleAndCoupleIds importResult.Couples []

    FamilyGraph.huwilp graph =! Set.empty

[<Fact>]
let ``transform huwilp with neither name nor pdeek emits WilpMissingNameAndPdeek`` () =
    let aliceWithEmpty = aliceRaw |> withWilp 3
    let empty = { Id = 3; Name = None; Pdeek = None }

    transform (rawFile [ aliceWithEmpty ] [] [ empty ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
        Couples = [ expectedCouple ]
        Warnings = expectedWarnings
    }

[<Theory>]
[<MemberData(nameof birthOrderCases)>]
let ``transform BirthOrder uses supplied value or defaults to zero`` (supplied: int option) (expected: int) =
    // Drive BirthOrder straight from the theory input so the `None` row genuinely
    // exercises the absent-birthOrder path regardless of `rawPerson`'s own default.
    let p = { rawPerson 0 "A" "F" with BirthOrder = supplied }

    let expectedPerson = { personOf 0 "A" Sphere with BirthOrder = expected }

    transform (rawFile [ p ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (expectedPerson, None) ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform copies the JSON person id into PersonId`` () =
    transform (rawFile [ rawPerson 7 "A" "F"; rawPerson 42 "B" "M" ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ personOf 7 "A" Sphere, None; personOf 42 "B" Cube, None ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform copies the JSON coupleId into CoupleId`` () =
    let c = rawCouple 250 0 1

    transform (rawFile [ aliceRaw; bobRaw ] [ c ] [])
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        NameHoldings = []
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
        NameHoldings = []
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
        NameHoldings = []
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
            NameHoldings = []
            Couples = []
            Warnings = []
        })

// ---------------------------------------------------------------------------
// transform: new person fields (colonial name, dates, kinship note, birth wilp)
// ---------------------------------------------------------------------------

let private heldName text : NameHeld = { Name = Name text; NameDate = None; NameOrder = Some 1 }

[<Fact>]
let ``transform maps an absent name to ColonialName None and names the warning by id`` () =
    let noName = aliceRaw |> withoutName |> withWilp 99

    transform (rawFile [ noName ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ ({ alice with ColonialName = None }, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedWilpId("#0", 99) ]
    }

[<Fact>]
let ``transform carries raw date strings into DateOfBirthText and DateOfDeathText`` () =
    let dated = aliceRaw |> withRawDob "circa 1850" |> withRawDod "1920-ish"

    transform (rawFile [ dated ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [
            ({
                alice with
                    DateOfBirthText = Some "circa 1850"
                    DateOfDeathText = Some "1920-ish"
             },
             None)
        ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform keeps a kinship note as NoneProvided Some when no wilp resolves`` () =
    let noted = aliceRaw |> withKinshipNote "raised by aunt"

    transform (rawFile [ noted ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ ({ alice with Kinship = NoneProvided(Some "raised by aunt") }, None) ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform keeps the kinship note alongside UnresolvedWilpId when the wilp is missing`` () =
    let noted = aliceRaw |> withWilp 99 |> withKinshipNote "note"

    transform (rawFile [ noted ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ ({ alice with Kinship = NoneProvided(Some "note") }, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedWilpId("Alice", 99) ]
    }

[<Fact>]
let ``transform drops the kinship note with IgnoredKinshipNote when a wilp resolves`` () =
    let w = rawWilp 2 "House" "Giskaast"
    let noted = aliceRaw |> withWilp 2 |> withKinshipNote "ignored"

    transform (rawFile [ noted ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [
            ({
                alice with
                    Kinship = Wilp { Name = WilpName "House"; Pdeek = Giskaast }
             },
             None)
        ]
        NameHoldings = []
        Couples = []
        Warnings = [ IgnoredKinshipNote "Alice" ]
    }

[<Fact>]
let ``transform resolves a named birthWilp into Person.BirthWilp`` () =
    let w = rawWilp 2 "House" "Giskaast"
    let bw = aliceRaw |> withBirthWilp 2

    transform (rawFile [ bw ] [] [ w ])
    =! Ok {
        PeopleAndCoupleIds = [
            ({
                alice with
                    BirthWilp = Some { Name = WilpName "House"; Pdeek = Giskaast }
             },
             None)
        ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform drops a pdeek-only birthWilp with BirthWilpNotNamed`` () =
    let pdeekOnly = { Id = 2; Name = None; Pdeek = Some "Ganeda" }
    let bw = aliceRaw |> withBirthWilp 2

    transform (rawFile [ bw ] [] [ pdeekOnly ])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ BirthWilpNotNamed("Alice", 2) ]
    }

[<Fact>]
let ``transform drops an unresolvable birthWilp with UnresolvedBirthWilpId`` () =
    let bw = aliceRaw |> withBirthWilp 99

    transform (rawFile [ bw ] [] [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedBirthWilpId("Alice", 99) ]
    }

// ---------------------------------------------------------------------------
// transform: names and namesHeld resolution
// ---------------------------------------------------------------------------

[<Fact>]
let ``transform resolves namesHeld rows into NameHoldings`` () =
    let names = [ rawName 10 "Tinker"; rawName 20 "Cobbler" ]
    let held = [ rawNameHeld 10 0; rawNameHeld 20 1 ]

    transform (rawFileWithNames [ aliceRaw; bobRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ alice, None; bob, None ]
        NameHoldings = [ PersonId 0, heldName "Tinker"; PersonId 1, heldName "Cobbler" ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform dedupes names by id keeping the first text and warns DuplicateNameId`` () =
    let names = [ rawName 10 "Tinker"; rawName 10 "Overwritten" ]
    let held = [ rawNameHeld 10 0 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, heldName "Tinker") ]
        Couples = []
        Warnings = [ DuplicateNameId 10 ]
    }

[<Fact>]
let ``transform collapses distinct ids with equal text to one Name and warns DuplicateNameText`` () =
    let names = [ rawName 10 "Tinker"; rawName 11 "Tinker" ]
    let held = [ rawNameHeld 10 0; rawNameHeld 11 0 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ PersonId 0, heldName "Tinker"; PersonId 0, heldName "Tinker" ]
        Couples = []
        Warnings = [ DuplicateNameText "Tinker" ]
    }

[<Fact>]
let ``transform drops a namesHeld row referencing an unknown name with UnresolvedNameId`` () =
    let names = [ rawName 10 "Tinker" ]
    let held = [ rawNameHeld 10 0; rawNameHeld 99 0 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, heldName "Tinker") ]
        Couples = []
        Warnings = [ UnresolvedNameId(0, 99) ]
    }

[<Fact>]
let ``transform drops a namesHeld row held by an unknown person with UnresolvedNameHolder`` () =
    let names = [ rawName 10 "Tinker" ]
    let held = [ rawNameHeld 10 0; rawNameHeld 10 99 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, heldName "Tinker") ]
        Couples = []
        Warnings = [ UnresolvedNameHolder(10, 99) ]
    }

[<Fact>]
let ``transform drops a name that nobody holds with UnheldName`` () =
    let names = [ rawName 10 "Tinker" ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names [])
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnheldName(10, "Tinker") ]
    }

[<Fact>]
let ``transform emits both UnresolvedNameHolder and UnheldName when a name's only holding is dropped`` () =
    // The bad holder never marks the name as referenced, so the name is left
    // with no *surviving* holding and is additionally reported unheld. Bad-row
    // warnings precede unheld-name warnings.
    let names = [ rawName 10 "Tinker" ]
    let held = [ rawNameHeld 10 99 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedNameHolder(10, 99); UnheldName(10, "Tinker") ]
    }

[<Fact>]
let ``transform accumulates name warnings in the order dup-id, dup-text, bad-rows, unheld`` () =
    // One input exercising all four name-warning classes at once pins the
    // documented accumulation order that the isolated tests can't. Name 30 keeps
    // a valid holding alongside its bad-holder row, so it is not also unheld.
    let names = [
        rawName 10 "Alpha" // kept
        rawName 10 "ShadowedById" // DuplicateNameId 10
        rawName 20 "Alpha" // DuplicateNameText "Alpha"
        rawName 30 "Beta" // held (valid + bad-holder row)
        rawName 40 "Gamma" // referenced by nobody -> UnheldName
    ]

    let held = [
        rawNameHeld 10 0 // valid -> name 10 referenced
        rawNameHeld 99 0 // unknown nameId -> UnresolvedNameId(0, 99)
        rawNameHeld 20 0 // valid -> name 20 referenced
        rawNameHeld 30 0 // valid -> name 30 referenced
        rawNameHeld 30 42 // unknown holder -> UnresolvedNameHolder(30, 42)
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [
            PersonId 0, heldName "Alpha"
            PersonId 0, heldName "Alpha"
            PersonId 0, heldName "Beta"
        ]
        Couples = []
        Warnings = [
            DuplicateNameId 10
            DuplicateNameText "Alpha"
            UnresolvedNameId(0, 99)
            UnresolvedNameHolder(30, 42)
            UnheldName(40, "Gamma")
        ]
    }

[<Fact>]
let ``transform reports an unheld duplicate-text name even when its text-twin is held`` () =
    // Name identity is its text, so the Name "Tinker" is held via id 10. But the
    // redundant *entry* (id 20) is referenced by no surviving holding, so it is
    // both a DuplicateNameText and an UnheldName — unheld detection is per-entry.
    let names = [ rawName 10 "Tinker"; rawName 20 "Tinker" ]
    let held = [ rawNameHeld 10 0 ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, heldName "Tinker") ]
        Couples = []
        Warnings = [ DuplicateNameText "Tinker"; UnheldName(20, "Tinker") ]
    }

[<Fact>]
let ``transform preserves the recency keys of a namesHeld row`` () =
    let names = [ rawName 10 "Tinker" ]

    let held = [
        {
            NameId = 10
            PersonId = 0
            NameDate = Some "1990-01-01"
            NameOrder = Some 3
        }
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [
            (PersonId 0, { Name = Name "Tinker"; NameDate = on 1990 1 1; NameOrder = Some 3 })
        ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform keeps but warns UnorderedNameHolding when a holding has no date and no order`` () =
    // The holding resolves (name and holder both exist) but carries no ordering
    // key, so it is kept and flagged so the caller knows it will sort alphabetically.
    let names = [ rawName 10 "Tinker" ]

    let held = [ { NameId = 10; PersonId = 0; NameDate = None; NameOrder = None } ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, { Name = Name "Tinker"; NameDate = None; NameOrder = None }) ]
        Couples = []
        Warnings = [ UnorderedNameHolding(10, 0) ]
    }

[<Fact>]
let ``transform interleaves an unordered-holding warning with other holding warnings in row order`` () =
    // A kept-but-unordered row and a dropped bad-name row are accumulated in
    // first-seen order, so UnorderedNameHolding sits among the other per-row
    // holding warnings rather than being segregated.
    let names = [ rawName 10 "Tinker" ]

    let held = [
        { NameId = 10; PersonId = 0; NameDate = None; NameOrder = None } // kept, unordered
        rawNameHeld 99 0 // unknown nameId, dropped
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, { Name = Name "Tinker"; NameDate = None; NameOrder = None }) ]
        Couples = []
        Warnings = [ UnorderedNameHolding(10, 0); UnresolvedNameId(0, 99) ]
    }

[<Fact>]
let ``transform warns both UnparseableNameDate and UnorderedNameHolding for an unparseable date with no order`` () =
    // The unparseable date is dropped to None and flagged (UnparseableNameDate);
    // having neither a parsed date nor a NameOrder then leaves no ordering key, so
    // the kept holding is additionally flagged UnorderedNameHolding.
    let names = [ rawName 10 "Tinker" ]

    let held = [
        {
            NameId = 10
            PersonId = 0
            NameDate = Some "not-a-date"
            NameOrder = None
        }
    ]

    // The two warnings come from a single row, so their order is the deterministic
    // source order (date warning before ordering warning); assert the whole result.
    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, { Name = Name "Tinker"; NameDate = None; NameOrder = None }) ]
        Couples = []
        Warnings = [ UnparseableNameDate(10, 0, "not-a-date"); UnorderedNameHolding(10, 0) ]
    }

[<Fact>]
let ``transform does not warn UnorderedNameHolding when a holding has a NameOrder`` () =
    // The unparseable date is dropped to None and flagged (UnparseableNameDate),
    // but the NameOrder still gives a well-defined order, so no UnorderedNameHolding.
    let names = [ rawName 10 "Tinker" ]

    let held = [
        {
            NameId = 10
            PersonId = 0
            NameDate = Some "not-a-date"
            NameOrder = Some 4
        }
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, { Name = Name "Tinker"; NameDate = None; NameOrder = Some 4 }) ]
        Couples = []
        Warnings = [ UnparseableNameDate(10, 0, "not-a-date") ]
    }

[<Fact>]
let ``transform does not warn UnorderedNameHolding when a holding has a parseable date`` () =
    // A parseable date is itself a well-defined order key, so no warning even
    // without a NameOrder.
    let names = [ rawName 10 "Tinker" ]

    let held = [
        {
            NameId = 10
            PersonId = 0
            NameDate = Some "1990-01-01"
            NameOrder = None
        }
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [
            (PersonId 0, { Name = Name "Tinker"; NameDate = on 1990 1 1; NameOrder = None })
        ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``transform does not warn UnparseableNameDate for a dropped unresolved holding`` () =
    // An unresolved nameId drops the whole row (UnresolvedNameId); its unparseable
    // date is not separately flagged, because the date is parsed only for holdings
    // that survive resolution — the dropped row no longer exists to carry one.
    let held = [
        {
            NameId = 99
            PersonId = 0
            NameDate = Some "not-a-date"
            NameOrder = None
        }
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] [] held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedNameId(0, 99) ]
    }

[<Fact>]
let ``transform does not warn UnparseableNameDate for a dropped holding with an unknown holder`` () =
    // The holder personId is unknown, so the row drops (UnresolvedNameHolder) on a
    // different branch than an unresolved name; as there too, its unparseable date
    // is not parsed or flagged. The Name is then held by nobody, so it is unheld.
    let names = [ rawName 10 "Tinker" ]

    let held = [
        {
            NameId = 10
            PersonId = 99
            NameDate = Some "not-a-date"
            NameOrder = None
        }
    ]

    transform (rawFileWithNames [ aliceRaw ] [] [] names held)
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = []
        Couples = []
        Warnings = [ UnresolvedNameHolder(10, 99); UnheldName(10, "Tinker") ]
    }

// ---------------------------------------------------------------------------
// Transform.toJson: FamilyGraph → JSON
//
// toJson is the inverse of fromJson for any graph built from a clean import.
// The strongest test is the round trip: build a graph from model data, write
// it out, read it back, and confirm the people and couples are reconstructed
// with no warnings. The fixtures below are chosen to cover every Kinship case
// (known Wilp, Pdeek-only UnknownWilp, NoneProvided), childless couples, dates,
// and birth order.
// ---------------------------------------------------------------------------

/// A Pdeek-only affiliation and a fully-known Wilp sharing that Pdeek.
let private pdeekOnlyPerson = {
    Person.Empty with
        Id = PersonId 300
        ColonialName = Some "PdeekOnly"
        Kinship = UnknownWilp LaxGibuu
}

let private knownWilpPerson = {
    Person.Empty with
        Id = PersonId 301
        ColonialName = Some "Known"
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
    let graph = FamilyGraph.createFamilyGraph peopleAndParents couples []

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
    let graph = FamilyGraph.createFamilyGraph testPeopleAndParents testCouples []

    match Transform.toJson graph |> JsonReader.read with
    | Ok raw -> raw.Huwilp |> List.length =! 2
    | Error error -> failwithf "toJson produced unreadable JSON: %A" error

// ---------------------------------------------------------------------------
// Transform.toJson: names, birth wilp, and kinship note synthesis
// ---------------------------------------------------------------------------

let private readBack graph =
    match Transform.toJson graph |> JsonReader.read with
    | Ok raw -> raw
    | Error error -> failwithf "toJson produced unreadable JSON: %A" error

/// Round-trips a graph through `toJson` then `fromJson`, returning the reimported
/// `ImportResult` (failing the test on an unexpected `Error`). Lifts the success
/// value to the top level so tests assert on it directly. Prefer a plain
/// `Transform.toJson graph |> fromJson =! Ok { … }` when the expected value can be
/// a literal; use this only when order-independent (set) comparisons are needed.
let private roundTrip graph =
    match Transform.toJson graph |> fromJson with
    | Ok result -> result
    | Error error -> failwithf "Round trip failed: %A" error

[<Fact>]
let ``toJson then fromJson reconstructs Name holdings warning-clean`` () =
    let held = { Name = Name "Tinker"; NameDate = on 1990 1 1; NameOrder = Some 1 }

    let graph =
        FamilyGraph.createFamilyGraph [ (alice, None) ] [] [ (PersonId 0, held) ]

    // The writer serializes the model DateOnly back to an ISO yyyy-MM-dd string
    // on disk (the round-trip below can't pin this, since fromJson parses loosely).
    (readBack graph).NamesHeld
    =! [
        {
            NameId = 0
            PersonId = 0
            NameDate = Some "1990-01-01"
            NameOrder = Some 1
        }
    ]

    Transform.toJson graph |> fromJson
    =! Ok {
        PeopleAndCoupleIds = [ (alice, None) ]
        NameHoldings = [ (PersonId 0, held) ]
        Couples = []
        Warnings = []
    }

[<Fact>]
let ``toJson collapses a Name handed down to several people into one names entry`` () =
    let held0 = { Name = Name "Tinker"; NameDate = None; NameOrder = None }
    let held1 = { Name = Name "Tinker"; NameDate = None; NameOrder = None }

    let graph =
        FamilyGraph.createFamilyGraph [ alice, None; bob, None ] [] [ PersonId 0, held0; PersonId 1, held1 ]

    // The handed-down Name collapses to a single names entry referenced by two
    // namesHeld rows (one per holder, in no guaranteed order). The length guards
    // multiplicity: Set.ofList alone would silently tolerate a duplicate row.
    let raw = readBack graph
    raw.Names =! [ { Id = 0; Text = "Tinker" } ]
    raw.NamesHeld |> List.length =! 2

    Set.ofList raw.NamesHeld
    =! set [
        { NameId = 0; PersonId = 0; NameDate = None; NameOrder = None }
        { NameId = 0; PersonId = 1; NameDate = None; NameOrder = None }
    ]

    // Round-tripping through fromJson retains both unordered holdings verbatim
    // (NameDate/NameOrder stay None) and re-reports each as UnorderedNameHolding.
    // Order isn't guaranteed, so compare as sets; length guards multiplicity.
    let reimported = roundTrip graph

    reimported.NameHoldings |> List.length =! 2

    Set.ofList reimported.NameHoldings
    =! set [ (PersonId 0, held0); (PersonId 1, held1) ]

    reimported.Warnings |> List.length =! 2

    Set.ofList reimported.Warnings
    =! set [ UnorderedNameHolding(0, 0); UnorderedNameHolding(0, 1) ]

[<Fact>]
let ``toJson omits the name field for a person with no colonial name`` () =
    let graph =
        FamilyGraph.createFamilyGraph [ ({ Person.Empty with Id = PersonId 0 }, None) ] [] []

    (readBack graph).People =! [ { defaultRawPerson with Id = 0 } ]

[<Fact>]
let ``toJson writes kinshipNote only for a NoneProvided Some kinship`` () =
    let noted = {
        Person.Empty with
            Id = PersonId 0
            Kinship = NoneProvided(Some "note")
    }

    let graph = FamilyGraph.createFamilyGraph [ (noted, None) ] [] []

    (readBack graph).People
    =! [ { defaultRawPerson with Id = 0; KinshipNote = Some "note" } ]

[<Fact>]
let ``toJson writes no kinshipNote for a resolved Wilp kinship`` () =
    let wilpPerson = {
        Person.Empty with
            Id = PersonId 0
            Kinship = Wilp { Name = WilpName "H"; Pdeek = Giskaast }
    }

    let graph = FamilyGraph.createFamilyGraph [ (wilpPerson, None) ] [] []

    (readBack graph).People =! [ { defaultRawPerson with Id = 0; Wilp = Some 0 } ]

[<Fact>]
let ``toJson gives a distinct birthWilp its own huwilp entry`` () =
    let wA = { Name = WilpName "A"; Pdeek = Giskaast }
    let wB = { Name = WilpName "B"; Pdeek = LaxGibuu }

    let person = {
        Person.Empty with
            Id = PersonId 0
            Kinship = Wilp wA
            BirthWilp = Some wB
    }

    let graph = FamilyGraph.createFamilyGraph [ (person, None) ] [] []
    let raw = readBack graph
    raw.Huwilp |> List.length =! 2

    // The kinship Wilp and the distinct birth Wilp get separate huwilp ids (0, 1),
    // so the person references two different entries.
    raw.People
    =! [ { defaultRawPerson with Id = 0; Wilp = Some 0; BirthWilp = Some 1 } ]

[<Fact>]
let ``toJson reuses one huwilp entry when kinship and birthWilp coincide`` () =
    let wA = { Name = WilpName "A"; Pdeek = Giskaast }

    let person = {
        Person.Empty with
            Id = PersonId 0
            Kinship = Wilp wA
            BirthWilp = Some wA
    }

    let graph = FamilyGraph.createFamilyGraph [ (person, None) ] [] []
    let raw = readBack graph
    raw.Huwilp |> List.length =! 1

    // Coinciding kinship and birth Wilp share the single huwilp id (0).
    raw.People
    =! [ { defaultRawPerson with Id = 0; Wilp = Some 0; BirthWilp = Some 0 } ]

[<Fact>]
let ``toJson then fromJson round-trips a distinct birthWilp via the union id map`` () =
    let wA = { Name = WilpName "A"; Pdeek = Giskaast }
    let wB = { Name = WilpName "B"; Pdeek = LaxGibuu }

    let person = {
        Person.Empty with
            Id = PersonId 0
            ColonialName = Some "A"
            Kinship = Wilp wA
            BirthWilp = Some wB
    }

    let graph = FamilyGraph.createFamilyGraph [ (person, None) ] [] []

    Transform.toJson graph |> fromJson
    =! Ok {
        PeopleAndCoupleIds = [ (person, None) ]
        NameHoldings = []
        Couples = []
        Warnings = []
    }
