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
let ``destroyAllConnectors removes all connector entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    world |> Connectors.spawnAllConnectors graph
    let connectorsBefore = world.Query(With Connector) |> Seq.length
    connectorsBefore >! 0
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
    connectorCount >! 0
    let lineCount = world.Query(With Line) |> Seq.length
    lineCount >! 0

[<Fact>]
let ``spawnAllConnectors creates elbow entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let graph = spawnTestScene world
    world |> Connectors.spawnAllConnectors graph
    let elbowCount = world.Query(With Elbow) |> Seq.length
    elbowCount >=! 1
