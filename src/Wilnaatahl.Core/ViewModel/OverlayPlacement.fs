namespace Wilnaatahl.ViewModel

/// The pixel co-ordinates at which an overlay card is rendered.
type OverlayPosition = { Left: float; Top: float }

module OverlayPlacement =
    let private gapPixels = 16.0
    let private marginPixels = 12.0

    /// Places an overlay beside a node, flipping and clamping it to the canvas when necessary.
    let place nodeLeft nodeRight nodeTop nodeBottom canvasWidth canvasHeight cardWidth cardHeight =
        let initialLeft = nodeRight + gapPixels

        let unclampedLeft =
            if initialLeft + cardWidth + marginPixels > canvasWidth then
                nodeLeft - gapPixels - cardWidth
            else
                initialLeft

        let left =
            unclampedLeft
            |> min (canvasWidth - cardWidth - marginPixels)
            |> max marginPixels

        let initialTop = nodeTop

        let unclampedTop =
            if initialTop + cardHeight + marginPixels > canvasHeight then
                nodeBottom - cardHeight
            else
                initialTop

        let top =
            unclampedTop
            |> min (canvasHeight - cardHeight - marginPixels)
            |> max marginPixels

        { Left = left; Top = top }
