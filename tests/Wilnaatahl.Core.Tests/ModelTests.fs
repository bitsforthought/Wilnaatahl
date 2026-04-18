module Wilnaatahl.Tests.ModelTests

open Xunit
open Swensen.Unquote
open System.Collections.Generic
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Tests.TestData

[<Fact>]
let ``findPerson returns correct person for all ids`` () =
    let graph = createFamilyGraph testPeopleAndParents
    let expectedPeople = [| p0; p1; p2; p3; p4 |]

    [ 0..4 ]
    |> List.iter (fun id -> findPerson (PersonId id) graph =! expectedPeople[id])

[<Fact>]
let ``findChildren returns correct children set for each parent`` () =
    let graph = createFamilyGraph testPeopleAndParents

    findChildren (PersonId 0) graph
    =! Set.ofList [ PersonId 2; PersonId 3; PersonId 4 ]

    findChildren (PersonId 1) graph
    =! Set.ofList [ PersonId 2; PersonId 3; PersonId 4 ]

    findChildren (PersonId 2) graph =! Set.empty
    findChildren (PersonId 3) graph =! Set.empty
    findChildren (PersonId 4) graph =! Set.empty

[<Fact>]
let ``createFamilyGraph handles empty input`` () =
    let graph = createFamilyGraph []
    // All public API should return empty/throw as appropriate
    <@ findPerson (PersonId 0) graph |> ignore @> |> raises<KeyNotFoundException>

    findChildren (PersonId 0) graph =! Set.empty
    coparents graph =! Set.empty

[<Fact>]
let ``coparents returns all co-parent relationships`` () =
    let graph = createFamilyGraph testPeopleAndParents
    let coParentsSet = coparents graph
    coParentsSet =! Set.ofList [ coParents ]

[<Fact>]
let ``huwilp returns all unique huwilp`` () =
    let graph = createFamilyGraph testPeopleAndParents
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
let ``Initial peopleAndParents assigns each Wilp to a distinct Pdeek`` () =
    // Wilp A is the primary matriline; per project conventions it is Giskaast (red) so
    // most visible nodes in the rendered tree retain the historical red appearance.
    let huwilpByName =
        Initial.peopleAndParents
        |> Seq.choose (fun (p, _) -> p.Wilp)
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
let ``visitWilpForest computes correct tree statistics`` () =
    let graph = createFamilyGraph extendedFamily
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

    let visitFamily parentId coParentsAndChildren =
        let processChildGroup (coParentId, childStats) =
            let combinedChildGroupStats = childStats |> aggregateStats

            {
                combinedChildGroupStats with
                    VisitedPeople = Array.append combinedChildGroupStats.VisitedPeople [| coParentId |]
                    NodeCount = combinedChildGroupStats.NodeCount + 1
            }

        let combinedDescendantStats =
            coParentsAndChildren |> Array.map processChildGroup |> aggregateStats

        {
            combinedDescendantStats with
                VisitedPeople = Array.append combinedDescendantStats.VisitedPeople [| parentId |]
                NodeCount = combinedDescendantStats.NodeCount + 1
                MaxDepth = combinedDescendantStats.MaxDepth + 1
        }

    // Reverse sort so we know sort is happening.
    let comparePeople p1 p2 = compare p2.Id p1.Id

    let totalStats =
        visitWilpForest wilp visitLeaf id id visitFamily comparePeople graph
        |> aggregateStats

    let expected = {
        NodeCount = 11
        LeafCount = 6
        MaxDepth = 3

        // Siblings will be sorted in descending order by ID, followed by their non-Wilp co-parent.
        // Groups of siblings under a co-parent are sorted before their Wilp parent.
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
    let graph = createFamilyGraph testPeopleAndParents
    let people = allPeople graph |> Seq.toList

    // Should contain all test data people, regardless of parentage
    people =! [ p0; p1; p2; p3; p4 ]

[<Fact>]
let ``visitWilpForest returns empty sequence for missing Wilp`` () =
    let graph = createFamilyGraph testPeopleAndParents
    let missingWilp = WilpName "Nonexistent"

    let results =
        visitWilpForest missingWilp (fun _ -> 0) id id (fun _ _ -> 0) compare graph
        |> Seq.toList

    results =! []
