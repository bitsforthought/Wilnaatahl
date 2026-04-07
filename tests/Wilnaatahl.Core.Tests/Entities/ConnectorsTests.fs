module Wilnaatahl.Tests.Entities.ConnectorsTests

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
    let graph = createFamilyGraph peopleAndParents
    let wilpId = world |> People.spawnWilpBox (WilpName "H")
    for person, _ in peopleAndParents do
        world |> People.spawnTreeNode person wilpId
    graph

[<Fact>]
let ``destroyAllConnectors removes all connector entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    world |> Connectors.spawnAllConnectors graph
    let connectorsBefore = world.Query(With Connector) |> Seq.length
    test <@ connectorsBefore > 0 @>
    world |> Connectors.destroyAllConnectors |> ignore
    let connectorsAfter = world.Query(With Connector) |> Seq.length
    connectorsAfter =! 0

[<Fact>]
let ``spawnAllConnectors creates connector entities for a family`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    world |> Connectors.spawnAllConnectors graph
    let connectorCount = world.Query(With Connector) |> Seq.length
    test <@ connectorCount > 0 @>
    let lineCount = world.Query(With Line) |> Seq.length
    test <@ lineCount > 0 @>

[<Fact>]
let ``spawnAllConnectors creates elbow entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    world |> Connectors.spawnAllConnectors graph
    let elbowCount = world.Query(With Elbow) |> Seq.length
    // 1 branch elbow + 1 junction elbow per child (3 children) = 4 elbows minimum
    test <@ elbowCount >= 4 @>
