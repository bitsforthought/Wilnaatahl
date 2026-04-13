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

let private mother = { Person.Empty with Id = PersonId 0; Shape = Sphere; Wilp = Some(WilpName "T") }
let private father = { Person.Empty with Id = PersonId 1; Shape = Cube }
let private child = { Person.Empty with Id = PersonId 2; Shape = Sphere; Wilp = Some(WilpName "T") }
let private coParents = { Mother = mother.Id; Father = father.Id }
let private testFamily = [ mother, None; father, None; child, Some coParents ]

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
    let graph = createFamilyGraph testFamily
    spawnScene world graph
    let personCount = world.Query(With PersonRef) |> Seq.length
    let connectorCount = world.Query(With Connector) |> Seq.length
    personCount =! 3
    connectorCount >! 0

[<Fact>]
let ``destroyScene removes all scene entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = createFamilyGraph testFamily
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
let ``destroyScene after spawnScene leaves controls intact`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnControls world
    let graph = createFamilyGraph testFamily
    spawnScene world graph
    destroyScene world |> ignore
    let buttonCount = world.Query(With Button) |> Seq.length
    buttonCount =! 3
