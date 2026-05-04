module Wilnaatahl.Entities.People

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.ViewModel.SceneConstants
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits

/// Spawns a Wilp box entity in the world and returns its EntityId.
let spawnWilpBox (wilp: WilpName) (world: IWorld) =
    // A Wilp box is itself a BoundingBox, so it is spawned via BoundingBox.spawn.
    let boundingBoxId, _, _ = world |> BoundingBox.spawn zeroPosition // TODO: Tweak Size.x to make huwilp forest look good
    boundingBoxId |> addWith RenderedWilp {| wilpName = wilp.AsString |} // TODO: Also add MeshRef/GroupRef/SceneRef to link this to a Three.js <group/>
    // TODO: Add Follows => Layout entity to track increasing z co-ordinate during layout.
    boundingBoxId

/// Spawns a tree node entity representing the given person in the specified wilp.
let spawnTreeNode person wilp (world: IWorld) =
    let nodeSize =
        let s = defaultSphereRadius
        let c = defaultCubeSize

        match person.Shape with
        | Sphere -> {| x = s; y = s; z = s |}
        | Cube -> {| x = c; y = c; z = c |}

    world.Spawn(PersonRef.Val person, Position.Val zeroPosition, Size.Val nodeSize, RenderedIn.ToTarget wilp)
    |> ignore
