namespace Wilnaatahl.ViewModel

type ProjectedNodeBounds = { Left: float; Right: float; Top: float; Bottom: float }

type internal ScreenRect = { Left: float; Right: float; Top: float; Bottom: float }

type internal LabelOcclusionInput = {
    Id: int
    Bounds: ProjectedNodeBounds
    ViewSpaceDepth: float
    LabelRect: ScreenRect option
}

type LabelPlacement = { X: float; Y: float; Scale: float }

/// DOM dimensions before the placement scale is applied.
type LabelSize = { Width: float; Height: float }

/// Numeric measurements for one node in a single layout frame.
type LabelRenderInput = {
    Id: int
    Bounds: ProjectedNodeBounds
    ViewSpaceDepth: float
    PixelsPerWorldUnit: float
    LabelSize: LabelSize option
}

/// The placement and visibility derived for one node in a layout frame.
type LabelRenderOutput = { Id: int; Placement: LabelPlacement; Visible: bool }

module LabelPlacement =
    // The gap is a screen-pixel tuning value, not a product-level minimum.
    let private labelGapPixels = 8.0

    // This reference scale preserves the existing label size at the default viewport.
    let private labelScalePerPixelPerWorldUnit = 0.0125

    /// Places a label by its top-centre screen point and derives its scale from the
    /// camera's pixels-per-world-unit measurement.
    let internal place (bounds: ProjectedNodeBounds) pixelsPerWorldUnit = {
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

    let private labelIsHiddenBy (nearerNode: LabelOcclusionInput) (targetNode: LabelOcclusionInput) =
        nearerNode.ViewSpaceDepth < targetNode.ViewSpaceDepth
        && (targetNode.LabelRect
            |> Option.exists (fun labelRect -> rectanglesOverlap labelRect nearerNode.Bounds)
            || targetNode.Bounds |> centreIsInside nearerNode.Bounds)

    let internal hiddenLabels (nodes: LabelOcclusionInput array) =
        nodes
        |> Array.choose (fun (targetNode: LabelOcclusionInput) ->
            if nodes |> Array.exists (fun nearerNode -> labelIsHiddenBy nearerNode targetNode) then
                Some targetNode.Id
            else
                None)

    let private scaledLabelRect placement labelSize =
        let width = labelSize.Width * placement.Scale
        let height = labelSize.Height * placement.Scale

        {
            Left = placement.X - width / 2.0
            Right = placement.X + width / 2.0
            Top = placement.Y
            Bottom = placement.Y + height
        }

    let private labelRect placement =
        function
        | Some labelSize when placement.Scale > 0.0 && labelSize.Width > 0.0 && labelSize.Height > 0.0 ->
            Some(scaledLabelRect placement labelSize)
        | _ -> None

    let private isFrontOfCamera viewSpaceDepth =
        viewSpaceDepth > 0.0 && not (System.Double.IsInfinity viewSpaceDepth)

    /// Derives placements and visibility without retaining measurements between frames.
    let layoutLabels (inputs: LabelRenderInput array) =
        let preparedInputs =
            inputs
            |> Array.map (fun input ->
                let placement = place input.Bounds input.PixelsPerWorldUnit
                let measuredLabelRect = input.LabelSize |> labelRect placement

                let occlusionInput: LabelOcclusionInput = {
                    Id = input.Id
                    Bounds = input.Bounds
                    ViewSpaceDepth = input.ViewSpaceDepth
                    LabelRect = measuredLabelRect
                }

                input, placement, occlusionInput)

        let hiddenIds =
            preparedInputs
            |> Array.map (fun (_, _, occlusionInput) -> occlusionInput)
            |> hiddenLabels
            |> Set.ofArray

        preparedInputs
        |> Array.map (fun (input, placement, occlusionInput) -> {
            Id = input.Id
            Placement = placement
            Visible =
                Option.isSome occlusionInput.LabelRect
                && isFrontOfCamera input.ViewSpaceDepth
                && (hiddenIds |> Set.contains input.Id |> not)
        })
