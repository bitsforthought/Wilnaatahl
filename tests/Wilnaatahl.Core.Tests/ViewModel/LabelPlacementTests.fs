module Wilnaatahl.Tests.ViewModel.LabelPlacementTests

open Swensen.Unquote
open Xunit
open Wilnaatahl.ViewModel

[<Fact>]
let ``place centres the label below the projected node`` () =
    let bounds = { Left = 120.0; Right = 280.0; Top = 200.0; Bottom = 360.0 }
    let pixelsPerWorldUnit = 2.0

    LabelPlacement.place bounds pixelsPerWorldUnit
    =! { X = 200.0; Y = 368.0; Scale = 0.025 }

[<Fact>]
let ``place keeps the label below a node whose bottom reaches the canvas edge`` () =
    let bounds = { Left = 0.0; Right = 80.0; Top = 560.0; Bottom = 600.0 }
    let pixelsPerWorldUnit = 1.5
    let scaleRoundingTolerance = 1e-12

    let actual = LabelPlacement.place bounds pixelsPerWorldUnit

    test
        <@
            actual.X = 40.0
            && actual.Y = 608.0
            && abs (actual.Scale - 0.01875) < scaleRoundingTolerance
        @>
