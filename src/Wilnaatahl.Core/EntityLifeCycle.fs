module Wilnaatahl.Systems.LifeCycle

open Wilnaatahl.ECS
open Wilnaatahl.ViewModel
open Wilnaatahl.Entities.Connectors
open Wilnaatahl.Entities.People
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo

/// Called at app startup to create entities that represent the scene controls.
let spawnControls (world: IWorld) =
    // Put the Undo/Redo controls to the left of the Select mode controls by
    // spawning them first.
    let initialSortOrder = 0

    (initialSortOrder, world)
    |> spawnUndoRedoControls
    |> spawnSelectControls
    |> ignore

/// Called during teardown of the App control to destroy all entities in the scene.
let destroyScene (world: IWorld) =
    // Destroy the connectors before the tree nodes (it shouldn't matter, but just for symmetry).
    world |> destroyAllConnectors |> destroyAllTreeNodes

/// Called during setup of the App control to create all entities in the scene.
let spawnScene (world: IWorld) familyGraph =
    // TODO: Spawn multiple huwilp once we support that.
    let huwilpMap = Scene.enumerateHuwilpToRender familyGraph
    let firstWilp, people = huwilpMap |> Seq.head |> (fun kvp -> kvp.Key, kvp.Value)

    let wilpId = world |> spawnWilpBox firstWilp

    // Spawn the tree nodes before connectors so the connectors can connect to them.
    for person in people do
        world |> spawnTreeNode person wilpId

    world |> spawnAllConnectors familyGraph
