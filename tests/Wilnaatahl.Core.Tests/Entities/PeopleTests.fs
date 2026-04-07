module Wilnaatahl.Tests.Entities.PeopleTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.ViewModel.SceneConstants
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

[<Fact>]
let ``spawnWilpBox creates bounding box with Wilp trait`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId = world |> People.spawnWilpBox (WilpName "TestWilp")

    boxId |> has Wilp =! true
    let wilpData = (boxId |> get Wilp).Value
    wilpData.wilpName =! "TestWilp"

[<Fact>]
let ``spawnWilpBox creates entity with Hidden and Connector traits`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let boxId = world |> People.spawnWilpBox (WilpName "W")

    boxId |> has Hidden =! true
    boxId |> has Connector =! true

[<Fact>]
let ``spawnTreeNode creates entity with PersonRef and Position`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    world |> People.spawnTreeNode p0 wilpId

    let nodes = world.Query(With PersonRef) |> Seq.toList
    nodes.Length =! 1
    let nodeId = nodes.Head
    nodeId |> has Position =! true
    let person = (nodeId |> get PersonRef).Value
    person.Id =! p0.Id

[<Fact>]
let ``spawnTreeNode uses sphere size for Sphere shape`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    world |> People.spawnTreeNode p0 wilpId // p0 has Shape = Sphere

    let nodeId = world.Query(With PersonRef) |> Seq.head
    let size = (nodeId |> get Size).Value
    let s = defaultSphereRadius
    size.x =! s
    size.y =! s
    size.z =! s

[<Fact>]
let ``spawnTreeNode uses cube size for Cube shape`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    world |> People.spawnTreeNode p1 wilpId // p1 has Shape = Cube

    let nodeId = world.Query(With PersonRef) |> Seq.head
    let size = (nodeId |> get Size).Value
    let c = defaultCubeSize
    size.x =! c
    size.y =! c
    size.z =! c

[<Fact>]
let ``spawnTreeNode adds RenderedIn relation to wilp`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    world |> People.spawnTreeNode p0 wilpId

    let nodeId = world.Query(With PersonRef) |> Seq.head
    nodeId |> targetFor RenderedIn =! Some wilpId

[<Fact>]
let ``destroyAllTreeNodes removes all person entities`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    world |> People.spawnTreeNode p0 wilpId
    world |> People.spawnTreeNode p1 wilpId

    let before = world.Query(With PersonRef) |> Seq.length
    before =! 2

    world |> People.destroyAllTreeNodes

    let after = world.Query(With PersonRef) |> Seq.length
    after =! 0
