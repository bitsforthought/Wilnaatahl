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
    let graph = createFamilyGraph peopleAndParents
    let wilpId = world |> People.spawnWilpBox (WilpName "H")
    for person, _ in peopleAndParents do
        world |> People.spawnTreeNode person wilpId
    graph

[<Fact>]
let ``layoutNodes sets TargetPosition on all tree nodes`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    layoutNodes world graph
    let treeNodes =
        world.QueryTrait(PersonRef).ToSequence() |> Seq.toList
    for _, entityId in treeNodes do
        let hasTarget = entityId |> has TargetPosition
        hasTarget =! true

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
