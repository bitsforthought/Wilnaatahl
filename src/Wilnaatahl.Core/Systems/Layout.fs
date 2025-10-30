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
        let visitLeaf pos personId offset =
            (personId, pos + offset) |> Seq.singleton

        let visitComposite pos results =
            results
            |> Seq.concat
            |> Seq.map (fun (personId, offset) -> personId, pos + offset)

        rootBox |> LayoutBox.visit visitLeaf visitComposite initialPosition

    world.QueryTrait(Wilp).ForEach
    <| fun (wilpData, wilpId) ->
        let wilp = WilpName wilpData.wilpName
        let layoutMap = Scene.layoutGraph wilp familyGraph |> setPositions |> Map.ofSeq

        world.QueryTrait(PersonRef, With(RenderedIn => wilpId)).ForEach
        <| fun (person: Person, treeNodeId) ->
            let pos = layoutMap |> Map.find person.Id

            treeNodeId
            |> addWith TargetPosition {| x = float pos.X; y = float pos.Y; z = float pos.Z |}
