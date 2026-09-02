module Wilnaatahl.Systems.ViewMode

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits

/// Discriminator marking the toolbar button that toggles between View (inspection) mode and
/// Move (manipulation) mode. It is a bare marker: the button holds no copy of the mode, only
/// the label naming the mode a click switches into.
let private ViewModeButton = tagTrait ()

/// Spawns the mode-toggle toolbar button and initializes the app in View mode.
let spawnViewModeControls (sortOrder, world: IWorld) =
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Move"; disabled = false |},
        ViewModeButton.Tag(),
        EmitsIntent.Val [ ChangeMode Moving ]
    )
    |> ignore

    world.AddWith CurrentMode Viewing

    sortOrder + 1, world

/// Makes every `MoveModeOnly` control hidden in View mode and shown in Move mode.
let syncModalControls (world: IWorld) =
    let matchMode =
        if world |> currentMode |> isViewing then
            add Hidden
        else
            remove Hidden

    for buttonEntity in world.Query(With MoveModeOnly) do
        buttonEntity |> matchMode

    world

/// Switches to `target` and updates the mode button for the next switch; repeated targets are
/// idempotent.
let private applyChangeMode buttonEntity target (world: IWorld) =
    if world |> currentMode <> target then
        let label = if isViewing target then "Move" else "View"
        let opposite = if isViewing target then Moving else Viewing

        world.Set CurrentMode target
        buttonEntity |> setWith Button (fun data -> {| data with label = label |})
        buttonEntity |> setValue EmitsIntent [ ChangeMode opposite ]

/// Applies this frame's `ChangeMode` intents and synchronizes modal controls without changing
/// selection.
let internal updateViewMode intents (world: IWorld) =
    let buttonEntity = world.Query(With ViewModeButton, With Button) |> Seq.exactlyOne

    intents
    |> List.iter (function
        | ChangeMode target -> world |> applyChangeMode buttonEntity target
        | _ -> ())

    world |> syncModalControls
