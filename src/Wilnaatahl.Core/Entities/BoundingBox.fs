module Wilnaatahl.Entities.BoundingBox

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.SpaceTraits

/// Marks an entity with Position as being a corner of the target BoundingBox entity. A BoundingBox should
/// have exactly two subject entities referring to it via this relation. One of the corners is closest to
/// the origin (the left-bottom-back corner, to fit the Three.js co-ordinate system which has higher X moving
/// right, higher Y moving up, and higher Z moving towards the camera). The other is its opposite corner
/// (right-top-front), indicated by IsBounds = true.
let private CornerOf =
    valueRelationWith {| IsBounds = false |} { IsExclusive = true }

let spawn size (world: IWorld) =
    let boxPosId = world.Spawn(Position.Val zeroPosition, Hidden.Tag())

    let boundPosId = world.Spawn(Position.Val zeroPosition, Hidden.Tag())

    let boxId = world.Spawn(Size.Val size, Hidden.Tag())
    boxPosId |> addRelationWith CornerOf boxId {| IsBounds = false |}
    boundPosId |> addRelationWith CornerOf boxId {| IsBounds = true |}
    boxId, boxPosId, boundPosId

let getCorners (world: IWorld) boxId =
    let corners = world.Query(Related(CornerOf, boxId)) |> Array.ofSeq

    if corners.Length = 2 then
        corners[0], corners[1]
    else
        failwith $"Found BoundingBox {boxId} with {corners.Length} corners."

let updateCorners (world: IWorld) changeOption f boxId =
    let mutable i = 0

    world.QueryTrait(Position, Related(CornerOf, boxId)).UpdateEachWith changeOption
    <| fun (pos, entity) ->
        i <- i + 1
        let which = entity |> getRelationValue CornerOf boxId |> Option.get

        match i with
        | 1 -> f pos which.IsBounds
        | 2 -> f pos which.IsBounds
        | _ -> failwith $"Found Line {boxId} with {i} endpoints during update."
