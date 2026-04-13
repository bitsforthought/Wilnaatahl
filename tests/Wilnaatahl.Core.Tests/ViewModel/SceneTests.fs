module Wilnaatahl.Tests.SceneTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ViewModel
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Tests.TestData
open Wilnaatahl.Tests.TestUtils

let private mapFamily (family: RenderedFamily<TestFamilyMember>) =
    let parent1, parent2 = family.Parents

    {|
        Parents = parent1.Id, parent2.Id
        Children = family.Children |> List.map _.Id
    |}

[<Fact>]
let ``ExtractFamilies produces correct results`` () =
    let graph = createFamilyGraph testPeopleAndParents

    let families =
        Scene.extractFamilies graph initialNodes |> Seq.toList |> List.map mapFamily

    families.Length =! 1
    let fam = families.Head
    fam.Parents =! (0, 1)

    Set.ofList fam.Children =! Set.ofList [ 2; 3; 4 ]

[<Fact>]
let ``layoutGraph assigns correct positions`` () =
    let graph = createFamilyGraph extendedFamily
    let rootOffset, rootBox = Scene.layoutGraph (WilpName "H") graph

    let actual =
        setPositions (rootOffset, rootBox)
        |> List.ofSeq
        |> List.sortBy (fun (p, _) -> p.AsInt)

    let expected = [
        PersonId 0, { X = -0.975<w>; Y = 0.0<w>; Z = 0.0<w> }
        PersonId 1, { X = 0.975<w>; Y = 0.0<w>; Z = 0.0<w> }
        PersonId 2, { X = -3.9<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 3, { X = -1.95<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 4, { X = 0.0<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 5, { X = 4.3875<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 6, { X = 1.95<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 7, { X = 6.825<w>; Y = -2.0<w>; Z = 0.0<w> }
        PersonId 8, { X = 3.9<w>; Y = -4.0<w>; Z = 0.0<w> }
        PersonId 9, { X = 1.95<w>; Y = -4.0<w>; Z = 0.0<w> }
        PersonId 10, { X = 5.85<w>; Y = -4.0<w>; Z = 0.0<w> }
    ]

    let areCoordinatesNearEqual a e = abs (a - e) <= LayoutBox.nearZero

    let areVectorsNearEqual a e =
        areCoordinatesNearEqual a.X e.X
        && areCoordinatesNearEqual a.Y e.Y
        && areCoordinatesNearEqual a.Z e.Z

    // Unforunately, due to the nature of floating point numbers, structural equality
    // isn't always going to work here. Instead, we iterate over the positions and
    // check co-ordinates with some tolerance.
    List.zip actual expected
    |> List.map (fun ((actualPersonId, actualOffset), (expectedPersonId, expectedOffset)) ->
        test
            <@
                actualPersonId = expectedPersonId
                && areVectorsNearEqual actualOffset expectedOffset
            @>)

[<Fact>]
let ``layoutGraph sorts children by DateOfBirth then BirthOrder`` () =
    // Build a family with children that exercise all 3 DateOfBirth comparison paths:
    //   childA (DoB 2000/6/1)  vs childB (DoB 2005/1/1) → dob1 < dob2
    //   childB (DoB 2005/1/1)  vs childA (DoB 2000/6/1) → dob1 > dob2
    //   childC (DoB 2000/6/1, BirthOrder=1) vs childA (DoB 2000/6/1, BirthOrder=0) → equal DoB, fallback to BirthOrder
    let mother = {
        Person.Empty with
            Id = PersonId 100
            Label = Some "Mother"
            Shape = Sphere
            Wilp = Some(WilpName "T")
    }

    let father = {
        Person.Empty with
            Id = PersonId 101
            Label = Some "Father"
            Shape = Cube
    }

    let childA = {
        Person.Empty with
            Id = PersonId 102
            Label = Some "ChildA"
            Shape = Sphere
            Wilp = Some(WilpName "T")
            DateOfBirth = Some(System.DateOnly(2000, 6, 1))
            BirthOrder = 0
    }

    let childB = {
        Person.Empty with
            Id = PersonId 103
            Label = Some "ChildB"
            Shape = Sphere
            Wilp = Some(WilpName "T")
            DateOfBirth = Some(System.DateOnly(2005, 1, 1))
    }

    let childC = {
        Person.Empty with
            Id = PersonId 104
            Label = Some "ChildC"
            Shape = Sphere
            Wilp = Some(WilpName "T")
            DateOfBirth = Some(System.DateOnly(2000, 6, 1))
            BirthOrder = 1
    }

    let parents = { Mother = mother.Id; Father = father.Id }

    let family = [
        mother, None
        father, None
        childA, Some parents
        childB, Some parents
        childC, Some parents
    ]

    let graph = createFamilyGraph family
    let _, rootBox = Scene.layoutGraph (WilpName "T") graph

    // Collect the X positions of children from the layout.
    let childPositions =
        setPositions ({ X = 0.0<w>; Y = 0.0<w>; Z = 0.0<w> }, rootBox)
        |> List.ofSeq
        |> List.choose (fun (pid, pos) ->
            match pid with
            | PersonId 102 | PersonId 103 | PersonId 104 -> Some(pid, pos.X)
            | _ -> None)
        |> List.sortBy snd

    // Expected sort order: childA (DoB 2000, order 0), childC (DoB 2000, order 1), childB (DoB 2005)
    let sortedIds = childPositions |> List.map fst
    sortedIds =! [ PersonId 102; PersonId 104; PersonId 103 ]
