module Wilnaatahl.Systems.LifeCycle

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.ViewModel
open Wilnaatahl.Entities.Connectors
open Wilnaatahl.Entities.People
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Systems.UndoRedo
open Wilnaatahl.Systems.ViewMode
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits

/// Called when entering the visualizer to create entities that represent the scene controls.
let spawnControls (world: IWorld) =
    let initialSortOrder = 0

    world.AddWith CurrentLocale En

    // The background is the single click target outside nodes and controls.
    world.Spawn(Background.Tag(), EmitsIntent.Val [ ClearSelection ]) |> ignore

    (initialSortOrder, world)
    |> spawnUndoRedoControls
    |> spawnSelectControls
    |> spawnViewModeControls
    |> spawnFileControls
    |> snd
    |> syncModalControls
    |> ignore

/// Whether a person's current Wilp differs from the Wilp they are rendered in. True for a person
/// with no named Wilp (`UnknownWilp`/`NoneProvided`), who has no name to match the rendered one.
let internal currentWilpDiffersFromRendered renderedWilpName (person: Person) =
    match person.Kinship with
    | Wilp w -> w.Name <> renderedWilpName
    | UnknownWilp _
    | NoneProvided _ -> true

/// Called during setup of the App control to create all entities in the scene.
let spawnScene (world: IWorld) familyGraph =
    // TODO: Spawn multiple huwilp once we support that.
    let huwilpMap = Scene.enumerateHuwilpToRender familyGraph
    let firstWilp, nodes = huwilpMap |> Seq.head |> (fun kvp -> kvp.Key, kvp.Value)

    let wilpId = world |> spawnWilpBox firstWilp

    // Spawn the tree nodes before connectors so the connectors can connect to them.
    for nodeKey, person in nodes do
        let label =
            NodeLabel.build
                person
                (familyGraph |> namesHeldBy person.Id)
                (currentWilpDiffersFromRendered firstWilp person)

        world |> spawnTreeNode person nodeKey label wilpId

    world |> spawnAllConnectors familyGraph
