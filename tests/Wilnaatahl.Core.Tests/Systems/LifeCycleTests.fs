module Wilnaatahl.Tests.Systems.LifeCycleTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnControls creates button entities``() =
        spawnControls world
        let buttonCount = world.Query(With Button) |> Seq.length
        // Undo, Redo, and Multi-select mode buttons
        buttonCount =! 3

    [<Fact>]
    member _.``spawnScene creates tree nodes and connectors``() =
        let graph = createFamilyGraph testPeopleAndParents
        spawnScene world graph
        let personCount = world.Query(With PersonRef) |> Seq.length
        let connectorCount = world.Query(With Connector) |> Seq.length
        personCount =! 5
        connectorCount >! 0

    [<Fact>]
    member _.``destroyScene removes all scene entities``() =
        let graph = createFamilyGraph testPeopleAndParents
        spawnScene world graph
        let personCountBefore = world.Query(With PersonRef) |> Seq.length
        personCountBefore >! 0
        let connectorCountBefore = world.Query(With Connector) |> Seq.length
        connectorCountBefore >! 0
        destroyScene world |> ignore
        let personCount = world.Query(With PersonRef) |> Seq.length
        let connectorCount = world.Query(With Connector) |> Seq.length
        personCount =! 0
        connectorCount =! 0

    [<Fact>]
    member _.``destroyScene after spawnScene leaves controls intact``() =
        spawnControls world
        let graph = createFamilyGraph testPeopleAndParents
        spawnScene world graph
        destroyScene world |> ignore
        let buttonCount = world.Query(With Button) |> Seq.length
        buttonCount =! 3
