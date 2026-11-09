namespace Wilnaatahl.ViewModel

open System

[<Measure>]
type cssPixel

[<Measure>]
type worldUnit

/// The projected bounds of a node in CSS pixels. The origin is at the canvas
/// top-left, so Y increases downward.
type ProjectedNodeBounds = {
    Left: float<cssPixel>
    Right: float<cssPixel>
    Top: float<cssPixel>
    Bottom: float<cssPixel>
}

/// The canvas dimensions in CSS pixels.
type CanvasDimensions = { Width: float<cssPixel>; Height: float<cssPixel> }

/// The top-centre CSS-pixel anchor and dimensionless scale for one label.
type LabelPlacement = { X: float<cssPixel>; Y: float<cssPixel>; Scale: float }

/// DOM dimensions measured in CSS pixels before the placement scale is applied.
type LabelSize = { Width: float<cssPixel>; Height: float<cssPixel> }

/// Numeric measurements for one projected node in a single layout frame.
type ProjectedNode = {
    Bounds: ProjectedNodeBounds
    ViewSpaceDepth: float<worldUnit>
    PixelsPerWorldUnit: float<cssPixel / worldUnit>
    LabelSize: LabelSize option
}

/// The placement and visibility derived for one projected node.
type LabelLayout = { Placement: LabelPlacement; Visible: bool }

module LabelPlacement =
    // The gap is a CSS-pixel tuning value, not a product-level minimum.
    let private labelGapPixels = 8.0<cssPixel>

    // This reference scale is world units per CSS pixel at the default viewport.
    let private labelWorldUnitsPerCssPixel = 0.0125<worldUnit / cssPixel>

    /// Places a label by its top-centre screen point and derives its dimensionless
    /// scale from the camera's CSS-pixels-per-world-unit measurement.
    let internal place
        (bounds: ProjectedNodeBounds)
        (pixelsPerWorldUnit: float<cssPixel / worldUnit>)
        : LabelPlacement = {
        X = (bounds.Left + bounds.Right) / 2.0
        Y = bounds.Bottom + labelGapPixels
        Scale = pixelsPerWorldUnit * labelWorldUnitsPerCssPixel
    }

    let private scaledLabelRect (placement: LabelPlacement) (labelSize: LabelSize) : ProjectedNodeBounds =
        let width = labelSize.Width * placement.Scale
        let height = labelSize.Height * placement.Scale

        {
            Left = placement.X - width / 2.0
            Right = placement.X + width / 2.0
            Top = placement.Y
            Bottom = placement.Y + height
        }

    let private labelRect (placement: LabelPlacement) =
        function
        | Some labelSize when
            placement.Scale > 0.0
            && labelSize.Width > 0.0<cssPixel>
            && labelSize.Height > 0.0<cssPixel>
            ->
            Some(scaledLabelRect placement labelSize)
        | _ -> None

    let private rectanglesOverlap (first: ProjectedNodeBounds) (second: ProjectedNodeBounds) =
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

    let private isFrontOfCamera (viewSpaceDepth: float<worldUnit>) =
        let positiveInfinity =
            LanguagePrimitives.FloatWithMeasure<worldUnit> Double.PositiveInfinity

        viewSpaceDepth > 0.0<worldUnit> && viewSpaceDepth <> positiveInfinity

    /// Derives one label layout from the frame's projected-node snapshot.
    let layoutLabel (projectedNodes: ProjectedNode array) (node: ProjectedNode) : LabelLayout =
        let placement = place node.Bounds node.PixelsPerWorldUnit
        let measuredLabelRect = node.LabelSize |> labelRect placement

        // TODO(#151): benchmark this quadratic occlusion scan in browser scenes
        // with 200+ nodes before considering a different algorithm.
        let hiddenByNearerNode =
            projectedNodes
            |> Array.exists (fun nearerNode ->
                nearerNode.ViewSpaceDepth < node.ViewSpaceDepth
                && (measuredLabelRect
                    |> Option.exists (fun labelRect -> rectanglesOverlap labelRect nearerNode.Bounds)
                    || node.Bounds |> centreIsInside nearerNode.Bounds))

        {
            Placement = placement
            Visible =
                measuredLabelRect.IsSome
                && isFrontOfCamera node.ViewSpaceDepth
                && not hiddenByNearerNode
        }
