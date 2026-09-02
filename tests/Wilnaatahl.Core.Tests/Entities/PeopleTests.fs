module Wilnaatahl.Tests.Entities.PeopleTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.ViewModel
open Wilnaatahl.ViewModel.SceneConstants
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let wilpId = world |> People.spawnWilpBox (WilpName "H")

    [<Fact>]
    member _.``spawnWilpBox creates bounding box with Wilp trait``() =
        let boxId = world |> People.spawnWilpBox (WilpName "TestWilp")

        boxId |> has RenderedWilp =! true
        let wilpData = (boxId |> get RenderedWilp).Value
        wilpData.wilpName =! "TestWilp"

    [<Fact>]
    member _.``spawnWilpBox creates entity with the Hidden trait``() =
        let boxId = world |> People.spawnWilpBox (WilpName "W")

        boxId |> has Hidden =! true

    [<Fact>]
    member _.``spawnTreeNode creates entity with PersonRef and Position``() =
        world |> People.spawnTreeNode p0 (MemberNode p0.Id) NodeLabelView.Empty wilpId

        let nodes = world.Query(With PersonRef) |> Seq.toList
        nodes.Length =! 1
        let nodeId = nodes.Head
        nodeId |> has Position =! true
        let person = (nodeId |> get PersonRef).Value
        person.Id =! p0.Id

    [<Fact>]
    member _.``spawnTreeNode stores the given label in the NodeLabel trait``() =
        let label = {
            NodeLabelView.Empty with
                ColonialName = Some "Mother"
                KinshipParen = Some "H"
        }

        world |> People.spawnTreeNode p0 (MemberNode p0.Id) label wilpId

        let nodeId = world.Query(With PersonRef) |> Seq.head
        nodeId |> has NodeLabel =! true
        (nodeId |> get NodeLabel).Value =! label

    [<Fact>]
    member _.``spawnTreeNode uses sphere size for Sphere shape``() =
        world |> People.spawnTreeNode p0 (MemberNode p0.Id) NodeLabelView.Empty wilpId // p0 has Shape = Sphere

        let nodeId = world.Query(With PersonRef) |> Seq.head
        let size = (nodeId |> get Size).Value
        let s = defaultSphereRadius
        size.x =! s
        size.y =! s
        size.z =! s

    [<Fact>]
    member _.``spawnTreeNode uses cube size for Cube shape``() =
        world |> People.spawnTreeNode p1 (MemberNode p1.Id) NodeLabelView.Empty wilpId // p1 has Shape = Cube

        let nodeId = world.Query(With PersonRef) |> Seq.head
        let size = (nodeId |> get Size).Value
        let c = defaultCubeSize
        size.x =! c
        size.y =! c
        size.z =! c

    [<Fact>]
    member _.``spawnTreeNode adds RenderedIn relation to wilp``() =
        world |> People.spawnTreeNode p0 (MemberNode p0.Id) NodeLabelView.Empty wilpId

        let nodeId = world.Query(With PersonRef) |> Seq.head
        nodeId |> targetFor RenderedIn =! Some wilpId

    [<Fact>]
    member _.``spawnTreeNode records a MemberNode's identity in NodeKeyRef``() =
        world |> People.spawnTreeNode p0 (MemberNode p0.Id) NodeLabelView.Empty wilpId

        let nodeId = world.Query(With PersonRef) |> Seq.head
        (nodeId |> get NodeKeyRef).Value =! MemberNode p0.Id

    [<Fact>]
    member _.``spawnTreeNode records a PartnerNode's identity, with its marriage CoupleId, in NodeKeyRef``() =
        world
        |> People.spawnTreeNode p1 (PartnerNode(p1.Id, coupleP0P1Id)) NodeLabelView.Empty wilpId

        let nodeId = world.Query(With PersonRef) |> Seq.head
        (nodeId |> get NodeKeyRef).Value =! PartnerNode(p1.Id, coupleP0P1Id)

    [<Fact>]
    member _.``spawnTreeNode declares an intent to toggle its own selection``() =
        world |> People.spawnTreeNode p0 (MemberNode p0.Id) NodeLabelView.Empty wilpId

        let nodeId = world.Query(With PersonRef) |> Seq.head
        (nodeId |> get EmitsIntent).Value =! [ ToggleNodeSelection nodeId ]

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
