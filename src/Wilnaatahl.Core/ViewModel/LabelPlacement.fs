namespace Wilnaatahl.ViewModel

type ProjectedNodeBounds = { Left: float; Right: float; Top: float; Bottom: float }

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
