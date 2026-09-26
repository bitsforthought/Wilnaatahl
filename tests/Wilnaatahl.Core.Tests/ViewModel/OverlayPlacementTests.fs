module Wilnaatahl.Tests.ViewModel.OverlayPlacementTests

open Swensen.Unquote
open Xunit
open Wilnaatahl.ViewModel

[<Theory>]
[<InlineData(100.0, 140.0, 50.0, 90.0, 800.0, 600.0, 200.0, 100.0, 156.0, 50.0)>]
[<InlineData(600.0, 760.0, 50.0, 90.0, 800.0, 600.0, 100.0, 100.0, 484.0, 50.0)>]
[<InlineData(100.0, 140.0, 550.0, 590.0, 800.0, 600.0, 200.0, 100.0, 156.0, 488.0)>]
[<InlineData(532.0, 572.0, 488.0, 528.0, 800.0, 600.0, 200.0, 100.0, 588.0, 488.0)>]
[<InlineData(-100.0, -50.0, 50.0, 90.0, 800.0, 600.0, 100.0, 100.0, 12.0, 50.0)>]
[<InlineData(100.0, 140.0, -20.0, 20.0, 800.0, 600.0, 200.0, 100.0, 156.0, 12.0)>]
[<InlineData(0.0, 20.0, 0.0, 20.0, 100.0, 100.0, 200.0, 150.0, 12.0, 12.0)>]
let ``place positions the card beside the node and respects canvas margins``
    nodeLeft
    nodeRight
    nodeTop
    nodeBottom
    canvasWidth
    canvasHeight
    cardWidth
    cardHeight
    expectedLeft
    expectedTop
    =
    let actual =
        OverlayPlacement.place nodeLeft nodeRight nodeTop nodeBottom canvasWidth canvasHeight cardWidth cardHeight

    actual =! { Left = expectedLeft; Top = expectedTop }
