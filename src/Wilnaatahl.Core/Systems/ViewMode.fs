module Wilnaatahl.Systems.ViewMode

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits

/// Discriminator marking the toolbar button that toggles between View (inspection) mode and
/// Move (manipulation) mode. It is a bare marker: the button holds no copy of the mode, only
/// the label naming the mode a click switches into.
let private ViewModeButton = tagTrait ()

/// Spawns the mode-toggle toolbar button and puts the app in its boot mode, View. The button's
/// label reads `Move` — the mode a click switches into, matching the select-mode button's label
/// convention. The mode button itself is available in both modes, so it carries no
/// `MoveModeOnly` marker.
let spawnViewModeControls (sortOrder, world: IWorld) =
    // The controls are spawned on app startup and never destroyed, which should be fine.
    world.Spawn(Button.Val {| sortOrder = sortOrder; label = "Move"; disabled = false |}, ViewModeButton.Tag())
    |> ignore

    // Enter the boot mode immediately so consumers observe it before the first frame runs.
    world.AddWith CurrentMode Viewing

    sortOrder + 1, world

/// Makes every `MoveModeOnly` control's `Hidden` state agree with the mode: hidden in View mode,
/// shown in Move mode. Callers spawning controls run this once so the toolbar never renders a
/// modal button before the first frame.
let syncModalControls (world: IWorld) =
    let matchMode =
        if world |> currentMode |> isViewing then
            add Hidden
        else
            remove Hidden

    for buttonEntity in world.Query(With MoveModeOnly) do
        buttonEntity |> matchMode

    world

/// Toggles the app mode when the mode-toggle button was clicked this frame, then makes the modal
/// controls match the resulting mode.
let updateViewMode (world: IWorld) =
    let buttonEntity = world.Query(With ViewModeButton, With Button) |> Seq.exactlyOne

    if buttonEntity |> wasClicked world then
        let switchingTo =
            if world |> currentMode |> isViewing then
                Moving
            else
                Viewing

        // The label reflects the mode a click switches into, so it shows the mode entered *next*:
        // after switching to Move the label reads "View"; after switching to View it reads "Move".
        let label = if isViewing switchingTo then "Move" else "View"

        world.Set CurrentMode switchingTo
        buttonEntity |> setWith Button (fun data -> {| data with label = label |})
        world.RemoveAll Selected

    world |> syncModalControls
