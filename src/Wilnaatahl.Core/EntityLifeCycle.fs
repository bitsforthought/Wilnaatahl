module Wilnaatahl.Systems.LifeCycle

open Wilnaatahl.ECS
open Wilnaatahl.ViewModel
open Wilnaatahl.Entities.Connectors
open Wilnaatahl.Entities.People
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo

/// Called when entering the visualizer to create entities that represent the scene controls.
let spawnControls (world: IWorld) =
    // Lay the toolbar out left to right by spawning in order: undo/redo, then the
    // select-mode control, then the open/save file controls.
    let initialSortOrder = 0

    (initialSortOrder, world)
    |> spawnUndoRedoControls
    |> spawnSelectControls
    |> spawnFileControls
    |> ignore

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
