namespace Wilnaatahl.Tests.ViewModel

open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Swensen.Unquote
open Wilnaatahl.ViewModel

/// FsCheck generators for vector properties. Components are restricted to
/// [-componentBound, componentBound] in fine steps, excluding the NaN,
/// infinity, and subnormal values FsCheck's default float generator would
/// otherwise emit. The bound keeps squared and cross-product intermediates
/// well within double precision so cancellation residuals stay far below the
/// comparison tolerance.
type internal VectorGenerators =
    static member private FloatGen: Gen<float> =
        // Components in [-100, 100] in steps of 1/1000.
        let componentBound = 100
        let stepsPerUnit = 1000
        let maxStep = componentBound * stepsPerUnit

        Gen.choose (-maxStep, maxStep)
        |> Gen.map (fun steps -> float steps / float stepsPerUnit)

    static member Float() = Arb.fromGen VectorGenerators.FloatGen

    static member Vector3() =
        gen {
            let! x = VectorGenerators.FloatGen
            let! y = VectorGenerators.FloatGen
            let! z = VectorGenerators.FloatGen
            return { x = x; y = y; z = z }
        }
        |> Arb.fromGen

[<Properties(Arbitrary = [| typeof<VectorGenerators> |])>]
module VectorTests =

    let private vec x y z : Vector3 = { x = x; y = y; z = z }

    let private zero = vec 0.0 0.0 0.0

    /// Combined absolute+relative tolerance. Pure float equality is too brittle
    /// across the magnitude range the generator produces; the absolute term
    /// keeps near-zero comparisons sane while the relative term scales with the
    /// operands.
    let private tolerance = 1e-6

    /// Tolerance for a dot product that is mathematically zero. Tighter than
    /// `tolerance` because an orthogonality residual comes from a single
    /// cross-then-dot round-trip rather than accumulated arithmetic.
    let private orthogonalityTolerance = 1e-9

    /// Vectors shorter than this are treated as degenerate for normalization;
    /// the zero-vector boundary is covered separately by an explicit fact.
    let private minNormalizableLength = 1e-6

    let private approxEq (a: float) (b: float) =
        abs (a - b) <= tolerance * (1.0 + max (abs a) (abs b))

    let private vecApproxEq (a: Vector3) (b: Vector3) =
        approxEq a.x b.x && approxEq a.y b.y && approxEq a.z b.z

    /// A dot product that is mathematically zero, checked against a tolerance
    /// scaled by the magnitudes of the operands so the bound tracks the size of
    /// the cancelling terms rather than a fixed absolute value.
    let private approxOrthogonal (u: Vector3) (v: Vector3) =
        let scale = Vector.length u * Vector.length v
        abs (u .* v) <= orthogonalityTolerance * (scale + 1.0)

    // --- Vector3 construction (non-inline statics: keep concrete coverage) ---

    [<Fact>]
    let ``FromPosition creates correct vector`` () =
        Vector3.FromPosition(Vector.zeroPosition) =! zero

    [<Fact>]
    let ``FromComponents creates correct vector`` () =
        Vector3.FromComponents(4.0, 5.0, 6.0) =! vec 4.0 5.0 6.0

    // --- Addition ---

    [<Fact>]
    let ``addition sums components`` () =
        vec 1.0 2.0 3.0 + vec 4.0 5.0 6.0 =! vec 5.0 7.0 9.0

    [<Property>]
    let ``addition is commutative`` (a: Vector3) (b: Vector3) = a + b = b + a

    [<Property>]
    let ``addition is associative`` (a: Vector3) (b: Vector3) (c: Vector3) = vecApproxEq ((a + b) + c) (a + (b + c))

    [<Property>]
    let ``the zero vector is the additive identity`` (a: Vector3) = a + zero = a

    // --- Subtraction ---

    [<Property>]
    let ``subtracting then adding the same vector is the identity`` (a: Vector3) (b: Vector3) =
        vecApproxEq ((a + b) - b) a

    // --- Cross product ---

    [<Fact>]
    let ``cross product of the x and y unit vectors is the z unit vector`` () =
        // Pins the sign that the anti-commutativity and orthogonality properties
        // leave free: a sign-flipped cross product satisfies both of those too.
        vec 1.0 0.0 0.0 * vec 0.0 1.0 0.0 =! vec 0.0 0.0 1.0

    [<Property>]
    let ``cross product is anti-commutative`` (a: Vector3) (b: Vector3) =
        // Swapping the operands negates the result, e.g. (1,2,3) × (4,5,6) = (-3,6,-3),
        // whereas (4,5,6) × (1,2,3) = (3,-6,3).
        a * b = zero - (b * a)

    [<Property>]
    let ``cross product is orthogonal to both operands`` (a: Vector3) (b: Vector3) =
        let cross = a * b
        approxOrthogonal cross a && approxOrthogonal cross b

    // --- Scalar multiplication ---

    [<Property>]
    let ``scalar multiplication is commutative`` (a: Vector3) (s: float) = a * s = s * a

    [<Property>]
    let ``scaling by zero yields the zero vector`` (a: Vector3) = vecApproxEq (a * 0.0) zero

    [<Property>]
    let ``scaling by one is the identity`` (a: Vector3) = a * 1.0 = a

    // --- Dot product ---

    [<Fact>]
    let ``dot product sums the componentwise products`` () =
        vec 1.0 2.0 3.0 .* vec 4.0 5.0 6.0 =! 32.0

    [<Property>]
    let ``dot product is commutative`` (a: Vector3) (b: Vector3) = a .* b = b .* a

    [<Property>]
    let ``a vector dotted with itself equals its length squared`` (a: Vector3) =
        // a · a = aₓ² + aᵧ² + a_z², which is exactly the squared Euclidean length.
        let len = Vector.length a
        approxEq (a .* a) (len * len)

    // --- Division ---

    [<Property>]
    let ``scaling up then dividing by the same scalar is the identity`` (a: Vector3) (s: float) =
        s <> 0.0 ==> lazy (vecApproxEq ((a * s) / s) a)

    // --- length ---

    [<Property>]
    let ``length is non-negative`` (a: Vector3) = Vector.length a >= 0.0

    [<Property>]
    let ``length scales by the absolute value of a scalar`` (a: Vector3) (s: float) =
        approxEq (Vector.length (a * s)) (abs s * Vector.length a)

    // --- normalize ---

    [<Property>]
    let ``normalize produces a unit vector for non-degenerate inputs`` (a: Vector3) =
        Vector.length a > minNormalizableLength
        ==> lazy (approxEq (Vector.length (Vector.normalize a)) 1.0)

    [<Fact>]
    let ``normalize of zero vector returns zero vector`` () = Vector.normalize zero =! zero

    // --- max / min ---

    [<Property>]
    let ``max is an upper bound of both operands componentwise`` (a: Vector3) (b: Vector3) =
        let m = Vector.max a b
        m.x >= a.x && m.x >= b.x && m.y >= a.y && m.y >= b.y && m.z >= a.z && m.z >= b.z

    [<Property>]
    let ``max is idempotent and commutative`` (a: Vector3) (b: Vector3) =
        Vector.max a a = a && Vector.max a b = Vector.max b a

    [<Property>]
    let ``min is a lower bound of both operands componentwise`` (a: Vector3) (b: Vector3) =
        let m = Vector.min a b
        m.x <= a.x && m.x <= b.x && m.y <= a.y && m.y <= b.y && m.z <= a.z && m.z <= b.z

    // --- lerp ---

    [<Property>]
    let ``lerp at alpha zero returns the start vector`` (a: Vector3) (b: Vector3) = vecApproxEq (Vector.lerp a b 0.0) a

    [<Property>]
    let ``lerp at alpha one returns the end vector`` (a: Vector3) (b: Vector3) = vecApproxEq (Vector.lerp a b 1.0) b

    [<Property>]
    let ``lerp is symmetric under swapping endpoints and complementing alpha``
        (a: Vector3)
        (b: Vector3)
        (alpha: float)
        =
        vecApproxEq (Vector.lerp a b alpha) (Vector.lerp b a (1.0 - alpha))

    // --- damp ---

    [<Property>]
    let ``damp with zero delta stays at the start`` (a: Vector3) (b: Vector3) (lambda: float) =
        vecApproxEq (Vector.damp a b lambda 0.0) a

    [<Property>]
    let ``damp never moves away from the target`` (a: Vector3) (b: Vector3) (lambda: float) (delta: float) =
        (lambda > 0.0 && delta > 0.0)
        ==> lazy
            (let remaining = Vector.length (b - Vector.damp a b lambda delta)
             let original = Vector.length (b - a)
             remaining <= original + tolerance * (1.0 + original))

    // --- MutableVector3 ---

    [<Fact>]
    let ``MutableVector3.Zero is all zeros`` () =
        let v = MutableVector3.Zero
        v.x =! 0.0
        v.y =! 0.0
        v.z =! 0.0

    [<Fact>]
    let ``MutableVector3.ToVector3 converts correctly`` () =
        let mv = { MutableVector3.Zero with x = 1.0; y = 2.0; z = 3.0 }
        mv.ToVector3() =! vec 1.0 2.0 3.0
