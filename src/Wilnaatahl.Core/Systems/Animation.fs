module Wilnaatahl.Systems.Animation

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ViewModel.SceneConstants
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Traits.SpaceTraits

let animate delta (world: IWorld) =
    world.QueryTraits(Position, TargetPosition).UpdateEachWith AlwaysTrack
    <| fun ((pos, targetPos), entity) ->
        let targetV, posV = targetPos.ToVector3(), pos.ToVector3()
        let lambda = animationDampRate
        let newV = damp posV targetV lambda delta

        // We need some tolerance due to funny business with IEEE 754 equality.
        // Vector3.nearZero is too close to make animation actually stop.
        let closeEnough = 0.01
        let deltaV = newV - targetV

        if
            Math.Abs deltaV.x < closeEnough
            && Math.Abs deltaV.y < closeEnough
            && Math.Abs deltaV.z < closeEnough
        then
            // Animation is finished; Set exactly to target and remove TargetPosition.
            pos.x <- targetV.x
            pos.y <- targetV.y
            pos.z <- targetV.z
            entity |> remove TargetPosition
        else
            pos.x <- newV.x
            pos.y <- newV.y
            pos.z <- newV.z

    world
