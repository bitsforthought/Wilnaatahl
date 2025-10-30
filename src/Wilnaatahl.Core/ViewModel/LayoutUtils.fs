namespace Wilnaatahl.ViewModel

open Wilnaatahl.Model

// MAINTENANCE NOTE: There is a separate vector implementation here with different casing for
// its fields in order to avoid ambiguity with the other vector types. I tried reusing a
// units-of-measure based vector for all use cases, but Fable seemed to generate invalid
// TypeScript code in that case, so I abandoned that approach.

/// Represents a vector in 3D space. The unit is specific to a frame of reference to
/// help prevent mixing up co-ordinate spaces in layout calculations.
type LayoutVector<[<Measure>] 'u> = {
    X: float<'u>
    Y: float<'u>
    Z: float<'u>
} with

    /// Adds two LayoutVectors together component-wise.
    static member inline (+)(lhs, rhs) =
        // MAINTENANCE NOTE: This won't show up in code coverage reports because it's inline, but don't worry about it.
        { X = lhs.X + rhs.X; Y = lhs.Y + rhs.Y; Z = lhs.Z + rhs.Z }

    /// Represents a zero vector in the given unit of measure.
    static member Zero = {
        X = LanguagePrimitives.FloatWithMeasure<'u> 0.0
        Y = LanguagePrimitives.FloatWithMeasure<'u> 0.0
        Z = LanguagePrimitives.FloatWithMeasure<'u> 0.0
    }

/// Definies utilities for working with LayoutVectors.
module LayoutVector =

    /// Changes the units of a vector. Unlike changing units of scalars, this has a runtime cost because
    /// it must allocate a new vector to hold the converted co-ordinates.
    let reframe<[<Measure>] 'u, [<Measure>] 'v>
        (conversionFactor: float<'v / 'u>)
        (vec: LayoutVector<'u>)
        : LayoutVector<'v> =
        {
            X = vec.X * conversionFactor
            Y = vec.Y * conversionFactor
            Z = vec.Z * conversionFactor
        }

/// Unit of measure (w means "world") that represents relative co-ordinates in the frame of reference of
/// the box resulting from a horizontal or vertical attach operation.
[<Measure>]
type w

/// Unit of measure (u means "upper") that represents relative co-ordinates in the frame of reference of
/// the upper box during a vertical attach operation.
[<Measure>]
type u

/// Unit of measure (l means "lower") that represents relative co-ordinates in the frame of reference of
/// the lower box during a vertical attach operation.
[<Measure>]
type l

/// Defines a frame of reference to help set the positions of points and other LayoutBoxes in world space.
type LayoutBox<[<Measure>] 'u> = {
    /// Size of the box in 3 dimensional space.
    Size: LayoutVector<'u>

    /// Distance from the left edge of the box to the point on its top edge where the "parent-child
    /// connector" should join. Used for alignment calculations when combining boxes.
    ConnectX: float<'u>

    /// Properties of the box that vary based on whether it's a leaf or composite box.
    Payload: LayoutBoxPayload<'u>
}

/// Contains the parts of a LayoutBox that vary based on whether it contains other LayoutBoxes or not.
and LayoutBoxPayload<[<Measure>] 'u> =
    | LeafBox of PersonId * LayoutVector<'u>
    | CompositeBox of CompositeLayoutBox<'u>

/// Defines properties specific to a Composite LayoutBox.
and CompositeLayoutBox<[<Measure>] 'u> = {
    /// Non-negative value indicating the horizontal distance from the left edge of the box to the left
    /// edge of its leftmost nested box that is vertically aligned on the top edge.
    TopLeftWidth: float<'u>

    /// Non-negative value indicating the horizontal distance from the right edge of the box to the right
    /// edge of its rightmost nested box that is vertically aligned on the top edge.
    TopRightWidth: float<'u>

    /// Collection of LayoutBoxes that are logically "contained" within this box and follow its origin at
    /// a given offset.
    Followers: (LayoutBox<'u> * LayoutVector<'u>) list
}

/// Options that control how two boxes are vertically attached.
type AttachAboveOptions = {
    /// If true, the upper box's ConnectX property is used as the basis for the combined box's ConnectX.
    /// Otherwise, the lower box's ConnectX property is used. Whichever one is used, it may be translated
    /// to maintain relative X position from the new box's origin, depending on how the boxes' left edges
    /// align vertically.
    UseUpperConnectX: bool

    /// Distance on the X axis from the origin of the lower box to the left edge of the upper box.
    /// If this value is negative, the upper box extends further left than the lower box, and conversely
    /// if this value is positive. If zero, the boxes vertically align on their left edges.
    UpperOffset: float<l>
}

module LayoutBox =
    // Define some conversion constants for our units.
    // MAINTENANCE NOTE: When multiplying quantities by these conversion factors, make sure the quantity represents
    // a pure magnitude (e.g. -- the width of a box). If it represents some kind of relative position, you're mis-using
    // the unit conversion and are likely to introduce bugs.

    /// Converts lower co-ordinates to world co-ordinates.
    let l2w = 1.0<w / l>

    /// Converts upper co-ordinates to lower co-ordinates.
    let u2l = 1.0<l / u>

    /// Converts upper co-ordinates to world co-ordinates.
    let u2w = 1.0<w / u>

    /// Converts world co-ordinates to lower co-ordinates.
    let w2l = 1.0<l / w>

    /// Converts world co-ordinates to upper co-ordinates.
    let w2u = 1.0<u / w>

    /// Small threshold to avoid numerical instability when normalizing vectors.
    let nearZero = 1e-9<w>

    /// Creates a leaf LayoutBox with the given property values.
    let createLeaf size connectX personId offset = {
        Size = size
        ConnectX = connectX
        Payload = LeafBox(personId, offset)
    }

    /// Creates a composite LayoutBox with the given property values.
    let createComposite size connectX composite = { Size = size; ConnectX = connectX; Payload = CompositeBox composite }

    /// Changes the units of a LayoutBox. Unlike changing units of scalars, this has a runtime cost because
    /// it must allocate a new box to hold the converted co-ordinates and vectors.
    let rec reframe<[<Measure>] 'u, [<Measure>] 'v>
        (conversionFactor: float<'v / 'u>)
        (box: LayoutBox<'u>)
        : LayoutBox<'v> =
        let reframePayload payload =
            let reframeFollower (follower, offset) =
                follower |> reframe conversionFactor, offset |> LayoutVector.reframe conversionFactor

            match payload with
            | LeafBox(personId, offset) -> LeafBox(personId, offset |> LayoutVector.reframe conversionFactor)
            | CompositeBox composite ->
                CompositeBox {
                    TopLeftWidth = composite.TopLeftWidth * conversionFactor
                    TopRightWidth = composite.TopRightWidth * conversionFactor
                    Followers = composite.Followers |> List.map reframeFollower
                }

        {
            Size = box.Size |> LayoutVector.reframe conversionFactor
            ConnectX = box.ConnectX * conversionFactor
            Payload = box.Payload |> reframePayload
        }

    /// Visits a LayoutBox and all its nested boxes, if any. The given callbacks are invoked for leaf and
    /// composite boxes, respectively. Each callback also takes a position for the box, which is taken
    /// from composite box followers, or the initial position passed in for the root box.
    let visit visitLeaf visitComposite initialPosition rootBox =
        let rec recurse (box, pos) =
            match box.Payload with
            | LeafBox(personId, offset) -> visitLeaf pos personId offset
            | CompositeBox composite -> composite.Followers |> Seq.map recurse |> visitComposite pos

        recurse (rootBox, initialPosition)

    /// Creates a new box followed by all the given boxes that lays them out horizontally,
    /// in the order given from left to right (lower to highers X co-ordinate), aligned to
    /// the highest Y co-ordinate of the tallest box and sized to the deepest Z size.
    let attachHorizontally (boxes: LayoutBox<w>[]) : LayoutBox<w> =
        // Base case is that there is just one box, in which case we return it.
        if boxes.Length = 1 then
            boxes[0]
        else
            // When combining leaf with composite boxes, see if we can pad them into the corners.
            let calculateNextDistance distanceToTheLeft (lhs, rhs) =
                let nextDistance =
                    match lhs.Payload, rhs.Payload with
                    | LeafBox _, LeafBox _ -> lhs.Size.X
                    | CompositeBox _, CompositeBox _ -> lhs.Size.X
                    | LeafBox _, CompositeBox composite when composite.TopLeftWidth <= nearZero -> lhs.Size.X
                    | CompositeBox composite, LeafBox _ when composite.TopRightWidth <= nearZero -> lhs.Size.X
                    | LeafBox _, CompositeBox composite -> lhs.Size.X - composite.TopLeftWidth
                    | CompositeBox composite, LeafBox _ -> lhs.Size.X - composite.TopRightWidth

                distanceToTheLeft + nextDistance

            let distancesToTheLeft = [| yield! boxes |> Array.pairwise |> Array.scan calculateNextDistance 0.0<w> |]

            let size = {
                X =
                    distancesToTheLeft[distancesToTheLeft.Length - 1]
                    + boxes[boxes.Length - 1].Size.X
                Y = boxes |> Seq.map _.Size.Y |> Seq.max
                Z = boxes |> Seq.map _.Size.Z |> Seq.max
            }

            let connectX =
                let boxCount = boxes.Length
                let middle = (boxCount - 1) / 2

                if boxCount % 2 <> 0 then
                    distancesToTheLeft[middle] + boxes[middle].ConnectX
                else
                    let left, right = boxes[middle], boxes[middle + 1]

                    // Trust me, it works, I did the math.
                    (2.0 * distancesToTheLeft[middle]
                     + left.ConnectX
                     + distancesToTheLeft[middle + 1]
                     + right.ConnectX)
                    / 2.0

            let followers =
                [ 0 .. boxes.Length - 1 ]
                |> List.map (fun i ->
                    boxes[i],
                    {
                        X = distancesToTheLeft[i]
                        Y = size.Y - boxes[i].Size.Y
                        Z = 0.0<w>
                    })

            let (leftmostCornerWidth, _), (_, rightmostCornerWidth) =
                let getCornerWidths box =
                    match box.Payload with
                    | LeafBox _ -> 0.0<_>, 0.0<_>
                    | CompositeBox composite -> composite.TopLeftWidth, composite.TopRightWidth

                boxes[0] |> getCornerWidths, boxes[boxes.Length - 1] |> getCornerWidths

            createComposite size connectX {
                TopLeftWidth = leftmostCornerWidth
                TopRightWidth = rightmostCornerWidth
                Followers = followers
            }

    /// Attaches two boxes vertically, taking into account possible skew on the X axis.
    /// The given offset is the position of the upper box relative to the lower on the X axis.
    /// If the value is 0, the two are aligned on the left edge. If it's negative, the upper box
    /// extends to the left of the lower one, and its left edge will be the zero X point of the new
    /// box. If it's positive, the lower box extends to the left of the upper one, and its left edge
    /// will be the zero X point of the new box. After attaching the boxes, the ConnectX of the new
    /// box is based on that of the lower box.
    let attachAbove (lowerBox: LayoutBox<l>) options (upperBox: LayoutBox<u>) : LayoutBox<w> =
        // These offsets are relative to the new origin, which is unitized as "world delta"
        let upperOffsetX = l2w * max options.UpperOffset 0.0<l>
        let lowerOffsetX = l2w * max -options.UpperOffset 0.0<l>

        // Take into account skew, so if one box doesn't completely encompass the other's width,
        // we get the correct overall width. Since the offset is relative to the lower box, it should
        // add to the upper box when positive, and add to the lower box when negative.
        let adjustedLowerBoxWidth = lowerOffsetX + lowerBox.Size.X * l2w
        let adjustedHigherBoxWidth = upperOffsetX + upperBox.Size.X * u2w

        let effectiveUpperHeight =
            if upperBox.Size.Y > nearZero * w2u then
                upperBox.Size.Y * u2w
            else
                0.0<w>

        let size = {
            X = max adjustedLowerBoxWidth adjustedHigherBoxWidth
            Y = effectiveUpperHeight + lowerBox.Size.Y * l2w
            Z = max (upperBox.Size.Z * u2w) (lowerBox.Size.Z * l2w)
        }

        // We also need to translate the X connector in case the offset is non-negative, because that means the
        // upper box is not vertically aligned to the left edge of the new world-space box (which is aligned to
        // the leftmost of the upper and lower boxes).
        let connectX =
            if options.UseUpperConnectX then
                upperOffsetX + upperBox.ConnectX * u2w
            else
                lowerOffsetX + lowerBox.ConnectX * l2w

        let lowerFollowerOffset = { X = lowerOffsetX; Y = 0.0<w>; Z = 0.0<w> }

        let upperFollowerOffset = { X = upperOffsetX; Y = lowerBox.Size.Y * l2w; Z = 0.0<w> }

        let followers = [
            lowerBox |> reframe l2w, lowerFollowerOffset
            upperBox |> reframe u2w, upperFollowerOffset
        ]

        // Distance to the right edge of the upper box relative to the combined box.
        let upperRightOffset = size.X - upperFollowerOffset.X - upperBox.Size.X * u2w

        // How we set the corners has to take into account what the corners of the lower box
        // were prior to combination, since the upper box may be zero height and actually
        // placed alongside existing nested boxes in the lower box.
        let topLeftWidth, topRightWidth =
            match lowerBox.Payload with
            | LeafBox _ -> upperFollowerOffset.X, upperRightOffset
            | CompositeBox _ when upperBox.Size.Y > nearZero * w2u -> upperFollowerOffset.X, upperRightOffset
            | CompositeBox composite ->
                min upperFollowerOffset.X (composite.TopLeftWidth * l2w),
                min upperRightOffset (composite.TopRightWidth * l2w)

        createComposite size connectX {
            TopLeftWidth = topLeftWidth
            TopRightWidth = topRightWidth
            Followers = followers
        }
