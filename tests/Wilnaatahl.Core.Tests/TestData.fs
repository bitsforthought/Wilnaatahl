module Wilnaatahl.Tests.TestData

open System
open Wilnaatahl.Model
open Wilnaatahl.ViewModel

type TestFamilyMember(id, person, wilp) =
    member _.Id: int = id

    interface IFamilyMemberInfo with
        member _.Person = person
        member _.RenderedInWilp = wilp

let private person id name shape wilp = {
    Person.Empty with
        Id = PersonId id
        Label = Some name
        Wilp = wilp
        Shape = shape
}

let testWilp = Some(WilpName "H")

// Test data is public because they are shared by other tests.
// Include some birthdates to exercise sorting.
let p0 = person 0 "Mother" Sphere testWilp
let p1 = person 1 "Father" Cube None

let p2 = {
    person 2 "Child1" Sphere testWilp with
        DateOfBirth = Some(DateOnly(1900, 1, 1))
        BirthOrder = 0
}

let p3 = {
    person 3 "Child2" Cube (Some(WilpName "L")) with
        DateOfBirth = Some(DateOnly(1900, 1, 1))
        BirthOrder = 1
}

let p4 = {
    person 4 "Child3" Cube testWilp with
        DateOfBirth = Some(DateOnly(1905, 1, 1))
}

let coParents = { Mother = p0.Id; Father = p1.Id }

let peopleAndParents = [
    p0, None
    p1, None
    p2, Some coParents
    p3, Some coParents
    p4, Some coParents
]

// Now we define an extended test data set to cover all corner cases.
let p5 = person 5 "Child4" Cube testWilp
let p6 = person 6 "DaughterInLaw1" Sphere None
let p7 = person 7 "DaughterInLaw2" Sphere None

let p8 = {
    person 8 "GrandChild1" Sphere testWilp with
        DateOfBirth = Some(DateOnly(1983, 1, 1))
}

let p9 = {
    person 9 "GrandChild2" Cube testWilp with
        DateOfBirth = Some(DateOnly(1979, 1, 1))
}

let p10 = person 10 "GrandChild3" Cube testWilp

let extendedFamily =
    peopleAndParents
    @ [
        p5, Some coParents
        p6, None
        p7, None
        p8, Some { Mother = p6.Id; Father = p5.Id }
        p9, Some { Mother = p6.Id; Father = p5.Id }
        p10, Some { Mother = p7.Id; Father = p5.Id }
    ]

let private treeNode id =
    let person = peopleAndParents |> List.find (fun (p, _) -> p.Id = PersonId id) |> fst
    TestFamilyMember(id, person, WilpName "H")

// Test data is public because they are shared by other tests.
let node0 = treeNode 0
let node1 = treeNode 1
let node2 = treeNode 2
let node3 = treeNode 3
let node4 = treeNode 4

let initialNodes = [ node0; node1; node2; node3; node4 ]
