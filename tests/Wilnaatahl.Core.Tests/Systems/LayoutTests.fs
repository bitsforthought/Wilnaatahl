module Wilnaatahl.Tests.Systems.LayoutTests

open System
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

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    [<Fact>]
    member _.``layoutNodes sets TargetPosition on all tree nodes`` () =
        let graph = spawnTestScene world
        layoutNodes world graph
        let personEntities = world.Query(With PersonRef) |> Set.ofSeq
        let animatingEntities = world.Query(With PersonRef, With TargetPosition) |> Set.ofSeq
        personEntities =! animatingEntities

    [<Fact>]
    member _.``layoutNodes assigns distinct positions to each person`` () =
        let graph = spawnTestScene world
        layoutNodes world graph
        let positions =
            world.QueryTrait(TargetPosition, With PersonRef).ToSequence()
            |> Seq.map fst
            |> Seq.toList
        let distinctPositions = positions |> List.distinct
        List.length positions =! List.length distinctPositions

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
