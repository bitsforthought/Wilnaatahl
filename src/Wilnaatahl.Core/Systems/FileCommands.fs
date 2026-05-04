module Wilnaatahl.Systems.FileCommands

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits

/// Discriminator marking the toolbar button that requests opening a file.
let internal OpenFileButton = tagTrait ()

/// Discriminator marking the toolbar button that requests saving a file.
let internal SaveButton = tagTrait ()

/// World signal raised when the open-file button is clicked. It is deliberately not
/// cleared during frame cleanup, so it persists until consumed and removed; a single
/// click therefore yields a single request that outlives the frame it was raised in.
let OpenFileRequested = tagTrait ()

/// World signal raised when the save button is clicked. Like OpenFileRequested, it
/// persists until consumed and removed rather than being cleared each frame.
let SaveRequested = tagTrait ()

/// Spawns the open-file and save toolbar buttons at consecutive sort orders.
let spawnFileControls (sortOrder, world: IWorld) =
    world.Spawn(Button.Val {| sortOrder = sortOrder; label = "Open file…"; disabled = false |}, OpenFileButton.Tag())
    |> ignore

    world.Spawn(Button.Val {| sortOrder = sortOrder + 1; label = "Save"; disabled = false |}, SaveButton.Tag())
    |> ignore

    sortOrder + 2, world

/// Maps a click on the open-file (resp. save) button to the matching world request
/// signal. Each button is checked independently, so raising one request never depends
/// on the state of the other.
let handleFileCommands (world: IWorld) =
    if world.QueryFirst(With OpenFileButton, With ClickEvent) |> Option.isSome then
        world.Add OpenFileRequested

    if world.QueryFirst(With SaveButton, With ClickEvent) |> Option.isSome then
        world.Add SaveRequested

    world
