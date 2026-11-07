namespace Wilnaatahl.ViewModel

type ProjectedNodeBounds = { Left: float; Right: float; Top: float; Bottom: float }

type ScreenRect = { Left: float; Right: float; Top: float; Bottom: float }

type LabelOcclusionInput = {
    Id: int
    Bounds: ProjectedNodeBounds
    ViewSpaceDepth: float
    LabelRect: ScreenRect option
}

type LabelPlacement = { X: float; Y: float; Scale: float }

module LabelPlacement =
    // The gap is a screen-pixel tuning value, not a product-level minimum.
    let private labelGapPixels = 8.0

    // This reference scale preserves the existing label size at the default viewport.
    let private labelScalePerPixelPerWorldUnit = 0.0125

    /// Places a label by its top-centre screen point and derives its scale from the
    /// camera's pixels-per-world-unit measurement.
    let place (bounds: ProjectedNodeBounds) pixelsPerWorldUnit = {
        X = (bounds.Left + bounds.Right) / 2.0
        Y = bounds.Bottom + labelGapPixels
        Scale = pixelsPerWorldUnit * labelScalePerPixelPerWorldUnit
    }

    let private rectanglesOverlap (first: ScreenRect) (second: ProjectedNodeBounds) =
        first.Left < second.Right
        && second.Left < first.Right
        && first.Top < second.Bottom
        && second.Top < first.Bottom

    let private centreIsInside (container: ProjectedNodeBounds) (bounds: ProjectedNodeBounds) =
        let centreX = (bounds.Left + bounds.Right) / 2.0
        let centreY = (bounds.Top + bounds.Bottom) / 2.0

        centreX >= container.Left
        && centreX <= container.Right
        && centreY >= container.Top
        && centreY <= container.Bottom

    let private labelIsHiddenBy nearerNode targetNode =
        nearerNode.ViewSpaceDepth < targetNode.ViewSpaceDepth
        && (targetNode.LabelRect
            |> Option.exists (fun labelRect -> rectanglesOverlap labelRect nearerNode.Bounds)
            || targetNode.Bounds |> centreIsInside nearerNode.Bounds)

    let hiddenLabels (nodes: LabelOcclusionInput array) =
        nodes
        |> Array.choose (fun targetNode ->
            if nodes |> Array.exists (fun nearerNode -> labelIsHiddenBy nearerNode targetNode) then
                Some targetNode.Id
            else
                None)
