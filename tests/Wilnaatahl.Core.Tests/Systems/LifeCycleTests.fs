module Wilnaatahl.Tests.Systems.LifeCycleTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Entities
open Wilnaatahl.System.Layout
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

    [<Fact>]
    member _.``spawnScene renders an outside spouse married to two members as two distinct non-crossing nodes``() =
        // The rendered Wilp "MM" has two members, each married to the same outside
        // spouse. The spouse must render as a separate node per marriage so the two
        // spouse-bars attach to distinct nodes rather than crossing at one shared node.
        let graph = createFamilyGraph multiMarriagePeople multiMarriageCouples
        spawnScene world graph
        layoutNodes world graph

        let personIdOf entity = (entity |> get PersonRef).Value.Id

        let isSpouseNode entity =
            personIdOf entity = multiMarriageSpouse.Id

        // 1. Two distinct node entities exist for the shared spouse.
        let spouseNodes =
            world.Query(With PersonRef) |> Seq.filter isSpouseNode |> Seq.toList

        spouseNodes.Length =! 2

        // 2. After layout, the two spouse nodes have distinct target positions.
        let spousePositions =
            world.QueryTrait(TargetPosition, With PersonRef).ToSequence()
            |> Seq.filter (fun (_, entity) -> isSpouseNode entity)
            |> Seq.map fst
            |> Seq.toList

        spousePositions.Length =! 2
        (spousePositions |> List.distinct).Length =! 2

        // 3. Each marriage's spouse-bar joins the correct member to the correct per-marriage
        // partner node. Both couples are childless, so each renders as exactly one hidden
        // spouse-bar Line whose two endpoints snap (via SnapToX) to the couple's two nodes.
        // Reconstructing the NodeKey of both endpoints pins the exact pairing, so a swap of
        // the two bars' targets — itself a crossing — would fail this assertion.
        let nodeKeyOf entity = (entity |> get NodeKeyRef).Value

        let barEndpointKeys =
            world.Query(With Line, With Hidden)
            |> Seq.map (fun line ->
                let firstEndpoint, secondEndpoint = line |> Line3.getEndpoints world

                [ firstEndpoint; secondEndpoint ]
                |> List.choose (targetFor SnapToX)
                |> List.map nodeKeyOf
                |> Set.ofList)
            |> Set.ofSeq

        let member1Key = MemberNode multiMarriageMember1.Id
        let member2Key = MemberNode multiMarriageMember2.Id
        let spouseKey1 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple1.Id)
        let spouseKey2 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple2.Id)

        barEndpointKeys
        =! Set.ofList [ Set.ofList [ member1Key; spouseKey1 ]; Set.ofList [ member2Key; spouseKey2 ] ]
