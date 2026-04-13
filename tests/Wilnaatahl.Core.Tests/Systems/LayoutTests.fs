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
open Wilnaatahl.Tests.TestData

let private spawnTestScene (world: IWorld) =
    let graph = createFamilyGraph testPeopleAndParents
    let wilpId = world |> People.spawnWilpBox testWilp.Value
    for person, _ in testPeopleAndParents do
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
        world.QueryTrait(TargetPosition, With PersonRef).ToSequence()
        |> Seq.map fst
        |> Seq.toList
    let distinctPositions = positions |> List.distinct
    List.length positions =! List.length distinctPositions
