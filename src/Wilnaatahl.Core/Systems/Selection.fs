module Wilnaatahl.Systems.Selection

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.ViewTraits

let private SelectModeButton = valueTrait {| multiSelect = true |}

let spawnSelectControls (sortOrder, world: IWorld) =
    // The controls are spawned on app startup and never destroyed, which should be fine.
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Multi-select"; disabled = false |},
        SelectModeButton.Val {| multiSelect = false |},
        MoveModeOnly.Tag()
    )
    |> ignore

    sortOrder + 1, world

let private handleSelectModeButtonClick buttonEntity multiSelect (world: IWorld) =
    if not (buttonEntity |> has ClickEvent) then
        false
    else
        // The label names the mode a click switches into, not the current one.
        let label = if multiSelect then "Multi-select" else "Single-select"

        buttonEntity |> setValue SelectModeButton {| multiSelect = not multiSelect |}
        buttonEntity |> setWith Button (fun data -> {| data with label = label |})
        // Leaving multiple nodes selected on the way into single-select mode would be confusing.
        world.RemoveAll Selected
        true

let private handleBackgroundClick (world: IWorld) =
    if world.Has PointerMissedEvent then
        world.RemoveAll Selected
        true
    else
        false

let private handleNodeClick multiSelect (world: IWorld) =
    let inViewMode = world.Has InViewMode

    // PersonRef stands in for tree nodes here, since only nodes mapping to people are selectable.
    match world.QueryFirst(With ClickEvent, With PersonRef) with
    | Some nodeEntity when nodeEntity |> has Selected ->
        nodeEntity |> remove Selected
        world
    | Some _ when inViewMode && not (world.Query(With Selected) |> Seq.isEmpty) ->
        // View mode inspects one node at a time, so a click elsewhere clears the selection
        // instead of selecting a different node.
        world.RemoveAll Selected
        world
    | Some nodeEntity ->
        // Nothing can still be selected in View mode by this point, so only Move mode's
        // multi-select setting decides whether to clear first.
        if not multiSelect then
            world.RemoveAll Selected

        nodeEntity |> add Selected
        world
    | None -> world

let selectNodes (world: IWorld) =
    let buttonData, buttonEntity =
        world.QueryTrait(SelectModeButton, With Button).ToSequence() |> Seq.exactlyOne

    let multiSelect = buttonData.multiSelect

    // Multi-touch makes every combination of these reachable in one frame, and no combination has
    // a coherent meaning, so precedence decides: the mode button beats the background, which
    // beats a node. The clicks that lose are dropped with the frame's events.
    if world |> handleSelectModeButtonClick buttonEntity multiSelect then
        world
    else if world |> handleBackgroundClick then
        world
    else
        world |> handleNodeClick multiSelect
