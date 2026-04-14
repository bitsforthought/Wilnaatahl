module Wilnaatahl.Tests.Entities.ConnectorsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
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

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``destroyAllConnectors removes all connector entities`` () =
        let graph = spawnTestScene world
        world |> Connectors.spawnAllConnectors graph
        let connectorsBefore = world.Query(With Connector) |> Seq.length
        connectorsBefore >! 0
        world |> Connectors.destroyAllConnectors |> ignore
        let connectorsAfter = world.Query(With Connector) |> Seq.length
        connectorsAfter =! 0

    [<Fact>]
    member _.``spawnAllConnectors creates connector entities for a family`` () =
        let graph = spawnTestScene world
        world |> Connectors.spawnAllConnectors graph
        let connectorCount = world.Query(With Connector) |> Seq.length
        connectorCount >! 0
        let lineCount = world.Query(With Line) |> Seq.length
        lineCount >! 0

    [<Fact>]
    member _.``spawnAllConnectors creates elbow entities`` () =
        let graph = spawnTestScene world
        world |> Connectors.spawnAllConnectors graph
        // With 3 children, we expect at least 1 branch elbow + 3 child junction elbows = 4.
        let elbowCount = world.Query(With Elbow) |> Seq.length
        elbowCount >=! 4
