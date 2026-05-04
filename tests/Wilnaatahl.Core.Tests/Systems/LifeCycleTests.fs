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
        // Undo, Redo, Multi-select mode, Open file, and Save buttons
        buttonCount =! 5

    [<Fact>]
    member _.``spawnScene creates tree nodes and connectors``() =
        let graph = createFamilyGraph testPeopleAndParents testCouples
        spawnScene world graph
        let personCount = world.Query(With PersonRef) |> Seq.length
        let lineCount = world.Query(With Line) |> Seq.length
        personCount =! 5
        lineCount >! 0
