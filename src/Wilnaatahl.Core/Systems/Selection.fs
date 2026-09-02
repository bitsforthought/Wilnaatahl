module Wilnaatahl.Systems.Selection

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits

let private SelectModeButton = valueTrait {| multiSelect = true |}

let spawnSelectControls (sortOrder, world: IWorld) =
    // The controls are spawned on app startup and never destroyed, which should be fine.
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Multi-select"; disabled = false |},
        SelectModeButton.Val {| multiSelect = false |},
        MoveModeOnly.Tag(),
        EmitsIntent.Val [ ToggleMultiSelect ]
    )
    |> ignore

    sortOrder + 1, world

/// The mode this frame's fold begins in: the mode stamped on the frame's first click, or the
/// world's current mode when the frame raised no clicks at all. A `ChangeMode` intent later in
/// the fold updates the running value from there, so this is only ever the starting point — it is
/// what lets the fold reach the same result regardless of which system happens to run first.
let private initialMode (world: IWorld) =
    world
    |> inputEvents
    |> Seq.tryPick (function
        | Clicked(_, mode) -> Some mode
        | DragStarted
        | Dragged _
        | DragEnded -> None)
    |> Option.defaultValue (world |> currentMode)

let private applyIntent buttonEntity (mode, multiSelect, world: IWorld) intent =
    match intent with
    | ChangeMode newMode ->
        // Switching modes clears the selection, regardless of where this intent falls among the
        // frame's other intents.
        if newMode <> mode then
            world.RemoveAll Selected

        newMode, multiSelect, world
    | ToggleMultiSelect ->
        // The label names the mode a click switches into, not the current one.
        let label = if multiSelect then "Multi-select" else "Single-select"

        buttonEntity |> setValue SelectModeButton {| multiSelect = not multiSelect |}
        buttonEntity |> setWith Button (fun data -> {| data with label = label |})
        // Leaving multiple nodes selected on the way into single-select mode would be confusing.
        world.RemoveAll Selected
        mode, not multiSelect, world
    | ToggleNodeSelection target ->
        let multiSelectForClick = multiSelect && not (isViewing mode)

        if target |> has Selected then
            target |> remove Selected
        elif not multiSelectForClick then
            world.RemoveAll Selected
            target |> add Selected
        else
            target |> add Selected

        mode, multiSelect, world
    | ClearSelection ->
        world.RemoveAll Selected
        mode, multiSelect, world
    | Undo
    | Redo
    | OpenFile
    | Save -> mode, multiSelect, world

/// Applies this frame's derived intents in order, folding a local mode/multi-select pair through
/// them so a `ChangeMode` intent changes how every intent after it in the same frame is
/// interpreted — not just the ones a later system happens to process.
let internal selectNodes intents (world: IWorld) =
    let buttonData, buttonEntity =
        world.QueryTrait(SelectModeButton, With Button).ToSequence() |> Seq.exactlyOne

    let _, _, world =
        intents
        |> List.fold (applyIntent buttonEntity) (initialMode world, buttonData.multiSelect, world)

    world
