module Wilnaatahl.Tests.Systems.LayoutTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.System.Layout
open Wilnaatahl.Tests.EcsTestSupport

let private mother = { Person.Empty with Id = PersonId 0; Shape = Sphere; Wilp = Some(WilpName "T") }
let private father = { Person.Empty with Id = PersonId 1; Shape = Cube }
let private child = { Person.Empty with Id = PersonId 2; Shape = Sphere; Wilp = Some(WilpName "T") }
let private coParents = { Mother = mother.Id; Father = father.Id }
let private testFamily = [ mother, None; father, None; child, Some coParents ]

let private spawnTestScene (world: IWorld) =
    let graph = createFamilyGraph testFamily
    let wilpId = world |> People.spawnWilpBox (WilpName "T")
    for person, _ in testFamily do
        world |> People.spawnTreeNode person wilpId
    graph

[<Fact>]
let ``layoutNodes sets TargetPosition on all tree nodes`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    layoutNodes world graph
    let personEntities = world.Query(With PersonRef) |> Set.ofSeq
    let animatingEntities = world.Query(With PersonRef, With TargetPosition) |> Set.ofSeq
    personEntities =! animatingEntities

[<Fact>]
let ``layoutNodes assigns distinct positions to each person`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    layoutNodes world graph
    let positions =
        world.QueryTrait(PersonRef).ToSequence()
        |> Seq.map (fun (_, entityId) -> entityId |> get TargetPosition)
        |> Seq.choose id
        |> Seq.map (fun pos -> pos.x, pos.y, pos.z)
        |> Seq.toList
    let distinctPositions = positions |> List.distinct
    List.length positions =! List.length distinctPositions
