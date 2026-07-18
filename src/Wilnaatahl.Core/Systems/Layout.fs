module Wilnaatahl.System.Layout

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.ViewModel
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits

let layoutNodes (world: IWorld) familyGraph =
    let setPositions (initialPosition, rootBox) =
        let visitLeaf pos nodeKey offset =
            (nodeKey, pos + offset) |> Seq.singleton

        let visitComposite pos results =
            results
            |> Seq.concat
            |> Seq.map (fun (nodeKey, offset) -> nodeKey, pos + offset)

        rootBox |> LayoutBox.visit visitLeaf visitComposite initialPosition

    world.QueryTrait(RenderedWilp).ForEach
    <| fun (wilpData, wilpId) ->
        let wilp = WilpName wilpData.wilpName
        let layoutMap = Scene.layoutGraph wilp familyGraph |> setPositions |> Map.ofSeq

        world.QueryTrait(NodeKeyRef, Related(RenderedIn, wilpId)).ForEach
        <| fun (nodeKey: NodeKey, treeNodeId) ->
            let pos = layoutMap |> Map.find nodeKey

            treeNodeId
            |> addWith TargetPosition {| x = float pos.X; y = float pos.Y; z = float pos.Z |}
