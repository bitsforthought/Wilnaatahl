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

let private applySelectionClick buttonEntity target mode (multiSelect, world: IWorld) =
    if target = buttonEntity then
        // The label names the mode a click switches into, not the current one.
        let label = if multiSelect then "Multi-select" else "Single-select"

        buttonEntity |> setValue SelectModeButton {| multiSelect = not multiSelect |}
        buttonEntity |> setWith Button (fun data -> {| data with label = label |})
        // Leaving multiple nodes selected on the way into single-select mode would be confusing.
        world.RemoveAll Selected
        not multiSelect, world
    elif target |> has PersonRef then
        let multiSelectForClick = multiSelect && not (isViewing mode)

        if target |> has Selected then
            target |> remove Selected
        elif not multiSelectForClick then
            world.RemoveAll Selected
            target |> add Selected
        else
            target |> add Selected

        multiSelect, world
    else
        multiSelect, world

let private applyInput buttonEntity (multiSelect, world: IWorld) event =
    match event with
    | Clicked(target, mode) -> (multiSelect, world) |> applySelectionClick buttonEntity target mode
    | PointerMissed ->
        world.RemoveAll Selected
        multiSelect, world
    | DragStarted
    | Dragged _
    | DragEnded -> multiSelect, world

let selectNodes (world: IWorld) =
    let buttonData, buttonEntity =
        world.QueryTrait(SelectModeButton, With Button).ToSequence() |> Seq.exactlyOne

    // Read the private preference once; each click updates the local value for later events.
    let _, world =
        world
        |> inputEvents
        |> Seq.fold (applyInput buttonEntity) (buttonData.multiSelect, world)

    world
