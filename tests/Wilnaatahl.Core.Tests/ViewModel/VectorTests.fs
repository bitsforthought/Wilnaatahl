module Wilnaatahl.Tests.ViewModel.VectorTests

open Xunit
open Swensen.Unquote
open Wilnaatahl.ViewModel

let private vec x y z : Vector3 = { x = x; y = y; z = z }

// --- Vector3 construction ---

[<Fact>]
let ``FromPosition creates correct vector`` () =
    Vector3.FromPosition(Vector.zeroPosition) =! vec 0.0 0.0 0.0

[<Fact>]
let ``FromComponents creates correct vector`` () =
    Vector3.FromComponents(4.0, 5.0, 6.0) =! vec 4.0 5.0 6.0

// --- Vector3 operators ---

[<Fact>]
let ``Addition of two vectors`` () =
    vec 1.0 2.0 3.0 + vec 4.0 5.0 6.0 =! vec 5.0 7.0 9.0

[<Fact>]
let ``Subtraction of two vectors`` () =
    vec 5.0 7.0 9.0 - vec 1.0 2.0 3.0 =! vec 4.0 5.0 6.0

[<Fact>]
let ``Cross product of two vectors`` () =
    vec 1.0 0.0 0.0 * vec 0.0 1.0 0.0 =! vec 0.0 0.0 1.0

[<Fact>]
let ``Scalar multiplication vector times scalar`` () =
    vec 1.0 2.0 3.0 * 2.0 =! vec 2.0 4.0 6.0

[<Fact>]
let ``Scalar multiplication scalar times vector`` () =
    2.0 * vec 1.0 2.0 3.0 =! vec 2.0 4.0 6.0

[<Fact>]
let ``Dot product of two vectors`` () =
    vec 1.0 2.0 3.0 .* vec 4.0 5.0 6.0 =! 32.0

[<Fact>]
let ``Division by scalar`` () =
    vec 4.0 6.0 8.0 / 2.0 =! vec 2.0 3.0 4.0

// --- Vector module ---

[<Fact>]
let ``length of known vector`` () =
    Vector.length (vec 3.0 4.0 0.0) =! 5.0

[<Fact>]
let ``normalize of known vector`` () =
    Vector.normalize (vec 3.0 4.0 0.0) =! vec 0.6 0.8 0.0

[<Fact>]
let ``normalize of zero vector returns zero vector`` () =
    Vector.normalize (vec 0.0 0.0 0.0) =! vec 0.0 0.0 0.0

[<Fact>]
let ``max returns component-wise maximum`` () =
    Vector.max (vec 1.0 5.0 3.0) (vec 4.0 2.0 6.0) =! vec 4.0 5.0 6.0

[<Fact>]
let ``min returns component-wise minimum`` () =
    Vector.min (vec 1.0 5.0 3.0) (vec 4.0 2.0 6.0) =! vec 1.0 2.0 3.0

[<Theory>]
[<InlineData(0.0)>]
[<InlineData(1.0)>]
[<InlineData(0.5)>]
let ``lerp at boundary and midpoint alphas`` (alpha: float) =
    let v1 = vec 0.0 0.0 0.0
    let v2 = vec 10.0 20.0 30.0
    Vector.lerp v1 v2 alpha =! vec (10.0 * alpha) (20.0 * alpha) (30.0 * alpha)

[<Fact>]
let ``damp approaches target`` () =
    let result = Vector.damp (vec 0.0 0.0 0.0) (vec 10.0 10.0 10.0) 5.0 1.0
    // With lambda=5.0 and delta=1.0, alpha = 1 - exp(-5) ≈ 0.9933
    result.x >! 9.0
    result.y >! 9.0
    result.z >! 9.0
    result.x <! 10.0
    result.y <! 10.0
    result.z <! 10.0

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
