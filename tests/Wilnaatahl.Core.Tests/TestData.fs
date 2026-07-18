module Wilnaatahl.Tests.TestData

open System
open Wilnaatahl.Model
open Wilnaatahl.ViewModel

type TestFamilyMember(person: Person, wilp, nodeKey) =
    member _.Id: int = person.Id.AsInt

    interface IFamilyMemberInfo with
        member _.Person = person
        member _.RenderedInWilp = wilp
        member _.NodeKey = nodeKey

let private person id name shape kinship = {
    Person.Empty with
        Id = PersonId id
        Label = Some name
        Kinship = kinship
        Shape = shape
}

let testWilp = Wilp { Name = WilpName "H"; Pdeek = Giskaast }

/// The WilpName inside `testWilp`, exposed separately so consumers that need
/// the bare name don't have to pattern-match the Kinship to reach it.
let testWilpName = WilpName "H"

// Test data is public because they are shared by other tests.
// Include some birthdates to exercise sorting.
let p0 = person 0 "Mother" Sphere testWilp
let p1 = person 1 "Father" Cube NoneProvided

let p2 = {
    person 2 "Child1" Sphere testWilp with
        DateOfBirth = Some(DateOnly(1900, 1, 1))
        BirthOrder = 0
}

let p3 = {
    person 3 "Child2" Cube (Wilp { Name = WilpName "L"; Pdeek = Ganeda }) with
        DateOfBirth = Some(DateOnly(1900, 1, 1))
        BirthOrder = 1
}

let p4 = {
    person 4 "Child3" Cube testWilp with
        DateOfBirth = Some(DateOnly(1905, 1, 1))
}

let coupleP0P1 = Couple.create (CoupleId 0) p0.Id p1.Id None
let coupleP0P1Id = coupleP0P1.Id

let testCouples = [ coupleP0P1 ]

let testPeopleAndParents = [
    p0, None
    p1, None
    p2, Some coupleP0P1Id
    p3, Some coupleP0P1Id
    p4, Some coupleP0P1Id
]

// Now we define an extended test data set to cover all corner cases.
let p5 = person 5 "Child4" Cube testWilp
let p6 = person 6 "DaughterInLaw1" Sphere NoneProvided
let p7 = person 7 "DaughterInLaw2" Sphere NoneProvided

let p8 = {
    person 8 "GrandChild1" Sphere testWilp with
        DateOfBirth = Some(DateOnly(1983, 1, 1))
}

let p9 = {
    person 9 "GrandChild2" Cube testWilp with
        DateOfBirth = Some(DateOnly(1979, 1, 1))
}

let p10 = person 10 "GrandChild3" Cube testWilp

let coupleP5P6 = Couple.create (CoupleId 1) p5.Id p6.Id None
let coupleP5P7 = Couple.create (CoupleId 2) p5.Id p7.Id None

let extendedCouples = testCouples @ [ coupleP5P6; coupleP5P7 ]

let extendedFamily =
    testPeopleAndParents
    @ [
        p5, Some coupleP0P1Id
        p6, None
        p7, None
        p8, Some coupleP5P6.Id
        p9, Some coupleP5P6.Id
        p10, Some coupleP5P7.Id
    ]

// ---- Building blocks for SceneTests and other consumers that need a small,
// purpose-built fixture rather than the full extendedFamily. These intentionally
// occupy a separate PersonId / CoupleId range from the p0..p10 fixtures above so
// the two fixture sets can coexist in the same graph if a test wants to combine
// them.

/// Two unaffiliated Persons in a Wilp ("Q") whose Couple has no recorded children.
/// Useful for any test that needs a representative childless-Couple scenario without
/// re-deriving people, a Wilp, or a Couple from scratch.
let childlessWilp = Wilp { Name = WilpName "Q"; Pdeek = LaxSkiik }
let childlessHead = person 100 "Quinn" Sphere childlessWilp
let childlessPartner = person 101 "Robin" Cube NoneProvided

let childlessCouple =
    Couple.create (CoupleId 100) childlessHead.Id childlessPartner.Id None

/// Four Persons with no Wilp and no other attributes, useful for tests that need
/// some distinct anonymous Persons to construct Couples between (e.g. the
/// comparator tests in SceneTests). Small AsInt values keep diagnostics readable.
let anon1 = { Person.Empty with Id = PersonId 200 }
let anon2 = { Person.Empty with Id = PersonId 201 }
let anon3 = { Person.Empty with Id = PersonId 202 }
let anon4 = { Person.Empty with Id = PersonId 203 }

// Two unrelated Persons (no Couples, no parents) drawn from the same Pdeek (Ganeda).
// One has a fully-known Wilp ("K"); the other has only the Pdeek recorded. The
// shared Pdeek lets tests exercise the boundary between the `Wilp` and
// `UnknownWilp` Kinship cases without conflating other variables.

/// Person whose Kinship is a fully-known Wilp ("K", Ganeda).
let wilpKMember = {
    Person.Empty with
        Id = PersonId 210
        Kinship = Wilp { Name = WilpName "K"; Pdeek = Ganeda }
}

/// Person whose Kinship is Pdeek-only — Ganeda is known, specific Wilp is not.
let ganedaPdeekOnlyPerson = { Person.Empty with Id = PersonId 211; Kinship = UnknownWilp Ganeda }

// ---- Multi-marriage fixture: one outside spouse married to two distinct members
// of the same rendered Wilp ("MM"). This is the scenario the seed data never
// exercises — the spouse must render as a separate node per marriage so the two
// spouse-bars attach to distinct nodes instead of crossing at one shared node.

/// A rendered Wilp ("MM") used only by the multi-marriage fixture.
let multiMarriageWilp = Wilp { Name = WilpName "MM"; Pdeek = Giskaast }

/// First "MM" member married to the shared outside spouse.
let multiMarriageMember1 = person 300 "MMMember1" Sphere multiMarriageWilp

/// Second "MM" member married to the shared outside spouse.
let multiMarriageMember2 = person 301 "MMMember2" Sphere multiMarriageWilp

/// Outside spouse (Kinship "MMOut", not "MM") married to both "MM" members.
let multiMarriageSpouse =
    person 302 "MMSpouse" Cube (Wilp { Name = WilpName "MMOut"; Pdeek = Ganeda })

/// Marriage of the first "MM" member to the shared outside spouse.
let multiMarriageCouple1 =
    Couple.create (CoupleId 300) multiMarriageMember1.Id multiMarriageSpouse.Id None

/// Marriage of the second "MM" member to the shared outside spouse.
let multiMarriageCouple2 =
    Couple.create (CoupleId 301) multiMarriageMember2.Id multiMarriageSpouse.Id None

let multiMarriagePeople = [
    multiMarriageMember1, None
    multiMarriageMember2, None
    multiMarriageSpouse, None
]

let multiMarriageCouples = [ multiMarriageCouple1; multiMarriageCouple2 ]

// ---- Endogamy fixture: two members of the SAME rendered Wilp ("EN") married to
// each other, both roots. Each member appears as the other's partner, so a naive
// tree-role classification would emit a PartnerNode for each and render both members
// twice. The correct behaviour is one MemberNode per member and no PartnerNode,
// because neither partner is from outside the rendered Wilp.

/// A rendered Wilp ("EN") used only by the endogamy fixture.
let endogamyWilp = Wilp { Name = WilpName "EN"; Pdeek = Giskaast }

/// First "EN" member, married to the second "EN" member.
let endogamyMember1 = person 400 "EndogamyMember1" Sphere endogamyWilp

/// Second "EN" member, married to the first "EN" member.
let endogamyMember2 = person 401 "EndogamyMember2" Cube endogamyWilp

/// Marriage of the two "EN" members to each other.
let endogamyCouple =
    Couple.create (CoupleId 400) endogamyMember1.Id endogamyMember2.Id None

let endogamyPeople = [ endogamyMember1, None; endogamyMember2, None ]

let endogamyCouples = [ endogamyCouple ]

let private treeNode id =
    let person =
        testPeopleAndParents |> List.find (fun (p, _) -> p.Id = PersonId id) |> fst

    TestFamilyMember(person, WilpName "H", MemberNode person.Id)

// Test data is public because they are shared by other tests.
let node0 = treeNode 0

// p1 is the outside partner (NoneProvided) married to p0. Production surfaces an
// outside partner as a PartnerNode keyed by that marriage's CoupleId — not a
// MemberNode — so the fixture uses the realistic key shape here.
let node1 = TestFamilyMember(p1, WilpName "H", PartnerNode(p1.Id, coupleP0P1Id))
let node2 = treeNode 2
let node3 = treeNode 3
let node4 = treeNode 4

let initialNodes = [ node0; node1; node2; node3; node4 ]
