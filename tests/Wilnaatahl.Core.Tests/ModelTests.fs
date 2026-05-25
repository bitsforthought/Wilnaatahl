module Wilnaatahl.Tests.ModelTests

open System
open Xunit
open Swensen.Unquote
open System.Collections.Generic
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Tests.TestData

[<Fact>]
let ``findPerson returns correct person for all ids`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let expectedPeople = [| p0; p1; p2; p3; p4 |]

    [ 0..4 ]
    |> List.iter (fun id -> findPerson (PersonId id) graph =! expectedPeople[id])

[<Fact>]
let ``createFamilyGraph handles empty input`` () =
    let graph = createFamilyGraph [] []
    // All public API should return empty/throw as appropriate
    <@ findPerson (PersonId 0) graph |> ignore @> |> raises<KeyNotFoundException>

    couples graph |> Seq.toList =! []

[<Fact>]
let ``couples returns all couples`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let couplesSet = couples graph |> Set.ofSeq
    couplesSet =! Set.ofList testCouples

[<Fact>]
let ``findChildrenOfCouple returns children in input order for a procreative Couple`` () =
    // testPeopleAndParents lists p2, p3, p4 as children of coupleP0P1 in that order.
    let graph = createFamilyGraph testPeopleAndParents testCouples
    findChildrenOfCouple coupleP0P1 graph =! [ p2.Id; p3.Id; p4.Id ]

[<Fact>]
let ``findChildrenOfCouple returns the empty list for a childless Couple`` () =
    // Construct a Couple between two roots that no Person references as their parents.
    let childlessCouple = Couple.create (CoupleId 999) p0.Id p1.Id None

    let graph = createFamilyGraph [ p0, None; p1, None ] [ childlessCouple ]

    findChildrenOfCouple childlessCouple graph =! []

[<Fact>]
let ``findChildrenOfCouple returns the empty list for a Couple absent from the graph`` () =
    // The function's contract is keyed on CoupleId, and a Couple that the graph never
    // saw cannot have any children attributed to it.
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let strangerCouple = Couple.create (CoupleId 12345) p0.Id p1.Id None
    findChildrenOfCouple strangerCouple graph =! []

[<Fact>]
let ``huwilp returns all unique huwilp`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let huwilpSet = huwilp graph
    huwilpSet =! Set.ofList [ WilpName "H"; WilpName "L" ]

type private TreeStats = {
    NodeCount: int
    LeafCount: int
    MaxDepth: int
    VisitedPeople: PersonId[] // Use an array so we can observe sort order
}

[<Fact>]
let ``WilpName returns itself as a string`` () =
    let wilp = WilpName "Test"
    wilp.AsString =! "Test"

[<Fact>]
let ``Kinship.Pdeek exposes the Pdeek of a known Wilp`` () =
    let kinship = Wilp { Name = WilpName "K"; Pdeek = Giskaast }
    kinship.Pdeek =! Some Giskaast

[<Fact>]
let ``Kinship.Pdeek exposes the Pdeek of an UnknownWilp`` () =
    let kinship = UnknownWilp Ganeda
    kinship.Pdeek =! Some Ganeda

[<Fact>]
let ``Kinship.Pdeek is None for NoneProvided`` () = NoneProvided.Pdeek =! None

[<Fact>]
let ``Initial peopleAndParents assigns each Wilp to a distinct Pdeek`` () =
    // Wilp A is the primary matriline; per project conventions it is Giskaast (red) so
    // most visible nodes in the rendered tree retain the historical red appearance.
    let huwilpByName =
        Initial.peopleAndParents
        |> Seq.choose (fun (p, _) ->
            match p.Kinship with
            | Wilp w -> Some w
            | _ -> None)
        |> Seq.map (fun w -> w.Name, w)
        |> Map.ofSeq

    huwilpByName |> Map.find (WilpName "A")
    =! { Name = WilpName "A"; Pdeek = Giskaast }

    // All four Pdeek must be represented across the four huwilp so the visualization
    // exercises the full color palette.
    let representedPdeek =
        huwilpByName |> Map.values |> Seq.map (fun w -> w.Pdeek) |> Set.ofSeq

    representedPdeek =! Set.ofList [ Giskaast; Ganeda; LaxSkiik; LaxGibuu ]

[<Fact>]
let ``createFamilyGraph excludes UnknownWilp people from huwilp`` () =
    // A person whose specific Wilp is unknown contributes no WilpName to the
    // huwilp set, even if their Pdeek matches that of another person with a
    // fully known Wilp. The fully known Wilp is the only entry that should
    // appear in the set.
    let graph = createFamilyGraph [ wilpKMember, None; ganedaPdeekOnlyPerson, None ] []

    huwilp graph =! Set.singleton (WilpName "K")

[<Fact>]
let ``UnknownWilp person with no Couple does not appear in any forest`` () =
    // Parity with the long-standing NoneProvided behaviour: a Pdeek-only Person
    // who is not married to a Wilp head is silently absent from every rendered
    // forest. The shared Pdeek between `wilpKMember` and `ganedaPdeekOnlyPerson`
    // means a buggy implementation that grouped by Pdeek would mis-include
    // ganedaPdeekOnlyPerson in wilpKMember's forest.
    let graph = createFamilyGraph [ wilpKMember, None; ganedaPdeekOnlyPerson, None ] []

    let visited =
        huwilp graph
        |> Seq.collect (fun wilpName ->
            visitWilpForest wilpName id id id (fun parent _ -> parent) (fun _ _ -> 0) (fun _ _ -> 0) graph)
        |> Set.ofSeq

    visited =! Set.singleton wilpKMember.Id

[<Fact>]
let ``visitWilpForest computes correct tree statistics`` () =
    let graph = createFamilyGraph extendedFamily extendedCouples
    let wilp = WilpName "H"

    let aggregateStats stats = {
        NodeCount = stats |> Seq.sumBy _.NodeCount
        LeafCount = stats |> Seq.sumBy _.LeafCount
        MaxDepth = stats |> Seq.map _.MaxDepth |> Seq.max
        VisitedPeople = stats |> Seq.map _.VisitedPeople |> Array.concat
    }

    // Recursive visitors that accumulate stats in the return value
    let visitLeaf personId = {
        NodeCount = 1
        LeafCount = 1
        MaxDepth = 1
        VisitedPeople = [| personId |]
    }

    let visitFamily parentId partnersAndChildren =
        let processChildGroup (partnerId, childStats) =
            let combinedChildGroupStats = childStats |> aggregateStats

            {
                combinedChildGroupStats with
                    VisitedPeople = Array.append combinedChildGroupStats.VisitedPeople [| partnerId |]
                    NodeCount = combinedChildGroupStats.NodeCount + 1
            }

        let combinedDescendantStats =
            partnersAndChildren |> Array.map processChildGroup |> aggregateStats

        {
            combinedDescendantStats with
                VisitedPeople = Array.append combinedDescendantStats.VisitedPeople [| parentId |]
                NodeCount = combinedDescendantStats.NodeCount + 1
                MaxDepth = combinedDescendantStats.MaxDepth + 1
        }

    // For this test we want a deterministic reverse-by-PersonId order so we can verify
    // that sorting actually happens. visitWilpForest takes two comparators:
    //   - compareTrees orders descendants within a single Couple's child group.
    //   - compareGroups orders the groups themselves.
    // We sort both by the (reversed) PersonId of the eldest descendant so the test data
    // (which only contains procreative Couples, so groups always have a descendant)
    // produces a fully deterministic walk order.
    let comparePeopleReversed (p1: Person) (p2: Person) = compare p2.Id p1.Id

    let compareTrees (t1: WilpTree) (t2: WilpTree) =
        comparePeopleReversed (graph |> findPerson t1.Root) (graph |> findPerson t2.Root)

    let compareGroups (_, descendants1) (_, descendants2) =
        match descendants1, descendants2 with
        | t1 :: _, t2 :: _ -> compareTrees t1 t2
        | _ -> 0 // The fixture has no childless Couples, so this branch is unreachable.

    let totalStats =
        visitWilpForest wilp visitLeaf id id visitFamily compareTrees compareGroups graph
        |> aggregateStats

    let expected = {
        NodeCount = 11
        LeafCount = 6
        MaxDepth = 3

        // Siblings will be sorted in descending order by ID, followed by their non-Wilp partner.
        // Groups of siblings under a partner are sorted before their Wilp parent.
        VisitedPeople = [|
            PersonId 10
            PersonId 7
            PersonId 9
            PersonId 8
            PersonId 6
            PersonId 5
            PersonId 4
            PersonId 3
            PersonId 2
            PersonId 1
            PersonId 0
        |]
    }

    totalStats =! expected

[<Fact>]
let ``allPeople returns all people in the graph`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let people = allPeople graph |> Seq.toList

    // Should contain all test data people, regardless of parentage
    people =! [ p0; p1; p2; p3; p4 ]

[<Fact>]
let ``visitWilpForest returns empty sequence for missing Wilp`` () =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let missingWilp = WilpName "Nonexistent"

    let trivialCompareTrees (_: WilpTree) (_: WilpTree) = 0
    let trivialCompareGroups (_: Couple * WilpTree list) (_: Couple * WilpTree list) = 0

    let results =
        visitWilpForest missingWilp (fun _ -> 0) id id (fun _ _ -> 0) trivialCompareTrees trivialCompareGroups graph
        |> Seq.toList

    results =! []

[<Fact>]
let ``Couple.create canonicalizes Members so order does not matter`` () =
    let p1 = PersonId 7
    let p2 = PersonId 3
    let cId = CoupleId 42

    let c1 = Couple.create cId p1 p2 None
    let c2 = Couple.create cId p2 p1 None

    // Two Couples built from the same pair in either order must be structurally equal.
    c1 =! c2
    // The canonical order is lower PersonId first; p2 (id=3) precedes p1 (id=7).
    c1.Members =! (p2, p1)

[<Fact>]
let ``Couple.create rejects equal members`` () =
    let p = PersonId 5
    let cId = CoupleId 1
    raises<exn> <@ Couple.create cId p p None @>

[<Fact>]
let ``Couple.create stores Id and DateOfUnion as supplied`` () =
    let p1 = PersonId 1
    let p2 = PersonId 2
    let cId = CoupleId 99
    let date = DateOnly(2020, 6, 15)

    let c = Couple.create cId p1 p2 (Some date)
    c.Id =! cId
    c.DateOfUnion =! Some date

[<Fact>]
let ``createFamilyGraph throws when a Person references an unknown CoupleId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let people = [ p1, None; p2, Some(CoupleId 99) ]
    let couples: Couple list = []

    let ex = Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples |> ignore)

    test <@ ex.Message.Contains "99" @>

[<Fact>]
let ``createFamilyGraph throws when a Couple references an unknown PersonId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let unknown = PersonId 99
    let couple = Couple.create (CoupleId 0) p1.Id unknown None
    let people = [ p1, None; p2, None ]
    let couples = [ couple ]

    let ex = Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples |> ignore)

    test <@ ex.Message.Contains "99" @>

[<Fact>]
let ``createFamilyGraph throws on duplicate CoupleId`` () =
    let p1 = { Person.Empty with Id = PersonId 1 }
    let p2 = { Person.Empty with Id = PersonId 2 }
    let p3 = { Person.Empty with Id = PersonId 3 }
    let p4 = { Person.Empty with Id = PersonId 4 }
    let dupId = CoupleId 5
    let c1 = Couple.create dupId p1.Id p2.Id None
    let c2 = Couple.create dupId p3.Id p4.Id None
    let people = [ p1, None; p2, None; p3, None; p4, None ]
    let couples = [ c1; c2 ]

    let ex = Assert.ThrowsAny<exn>(fun () -> createFamilyGraph people couples |> ignore)

    test <@ ex.Message.Contains "5" @>

// Captures the tree shape produced by visitWilpForest for use in equality assertions.
type private CapturedTree =
    | CapturedLeaf of PersonId
    | CapturedFamily of wilpParent: PersonId * partnersAndDescendants: (PersonId * CapturedTree list) list

[<Fact>]
let ``buildWilpTree exposes childless Couples as empty PartnersAndDescendants entries`` () =
    let mWilp = {
        Person.Empty with
            Id = PersonId 100
            Kinship = Wilp { Name = WilpName "M"; Pdeek = Giskaast }
            Shape = Sphere
    }

    let pOutsider = { Person.Empty with Id = PersonId 101; Shape = Cube }

    let childlessCouple = Couple.create (CoupleId 200) mWilp.Id pOutsider.Id None
    let people = [ mWilp, None; pOutsider, None ]
    let couples = [ childlessCouple ]

    let graph = createFamilyGraph people couples

    let visitFamily wilpParent (partnerResults: (PersonId * CapturedTree seq)[]) =
        let partners =
            partnerResults
            |> Array.map (fun (cp, descs) -> cp, Seq.toList descs)
            |> List.ofArray

        CapturedFamily(wilpParent, partners)

    // This fixture has only one Couple so the comparators never fire on a non-empty
    // pair; trivial implementations are sufficient.
    let neverCompareTrees _ _ = 0
    let neverCompareGroups _ _ = 0

    let results =
        visitWilpForest (WilpName "M") CapturedLeaf id id visitFamily neverCompareTrees neverCompareGroups graph
        |> Seq.toList

    results =! [ CapturedFamily(mWilp.Id, [ pOutsider.Id, [] ]) ]
