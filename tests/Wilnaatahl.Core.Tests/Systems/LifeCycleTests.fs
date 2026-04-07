module Wilnaatahl.Tests.Systems.LifeCycleTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

[<Fact>]
let ``spawnControls creates button entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    let buttonCount = world.Query(With Button) |> Seq.length
    // Undo, Redo, and Multi-select mode buttons
    buttonCount =! 3

[<Fact>]
let ``spawnScene creates tree nodes and connectors`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = createFamilyGraph peopleAndParents
    spawnScene world graph
    let personCount = world.Query(With PersonRef) |> Seq.length
    let connectorCount = world.Query(With Connector) |> Seq.length
    personCount =! 5
    test <@ connectorCount > 0 @>

[<Fact>]
let ``destroyScene removes all scene entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = createFamilyGraph peopleAndParents
    spawnScene world graph
    destroyScene world |> ignore
    let personCount = world.Query(With PersonRef) |> Seq.length
    let connectorCount = world.Query(With Connector) |> Seq.length
    personCount =! 0
    connectorCount =! 0

[<Fact>]
let ``destroyScene after spawnScene leaves controls intact`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    let graph = createFamilyGraph peopleAndParents
    spawnScene world graph
    destroyScene world |> ignore
    let buttonCount = world.Query(With Button) |> Seq.length
    buttonCount =! 3
