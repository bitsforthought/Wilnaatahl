namespace Wilnaatahl.ViewModel

// MAINTENANCE NOTE: This file is performance-critical. Avoid introducing allocations in inner loops.
// Also, the field names are intentionally lower-case since they're used in TypeScript too.

/// Represents a 3-component vector with immutable components.
type Vector3 = {
    x: float
    y: float
    z: float
} with

    /// Creates a vector from an anonymous record with x, y, z fields.
    static member FromPosition(pos: {| x: float; y: float; z: float |}) = { x = pos.x; y = pos.y; z = pos.z }

    /// Creates a vector from a tuple of component values.
    static member FromComponents(x, y, z) = { x = x; y = y; z = z }

    // MAINTENANCE NOTE: Inline members won't show up in code coverage reports, but don't worry about it.

    /// Adds two vectors component-wise.
    static member inline (+)(v1, v2) = { x = v1.x + v2.x; y = v1.y + v2.y; z = v1.z + v2.z }

    /// Subtracts the second vector from the first component-wise.
    static member inline (-)(v1, v2) = { x = v1.x - v2.x; y = v1.y - v2.y; z = v1.z - v2.z }

    /// Calculates the cross product of two vectors.
    static member inline (*)(v1, v2) = {
        x = v1.y * v2.z - v1.z * v2.y
        y = v1.z * v2.x - v1.x * v2.z
        z = v1.x * v2.y - v1.y * v2.x
    }

    /// Multiplies a vector by a scalar.
    static member inline (*)(v, k) = { x = v.x * k; y = v.y * k; z = v.z * k }

    /// Multiplies a vector by a scalar.
    static member inline (*)(k: float, v: Vector3) = v * k

    /// Computes the dot product of two vectors.
    static member inline (.*)(v1, v2) = v1.x * v2.x + v1.y * v2.y + v1.z * v2.z

    /// Divides a vector by a scalar.
    static member inline (/)(v, k) = { x = v.x / k; y = v.y / k; z = v.z / k }

/// Module containing utility functions for Vector3 operations.
module Vector =

    /// Represents the zero vector as an anonymous record for storage purposes.
    let zeroPosition = {| x = 0.0; y = 0.0; z = 0.0 |}

    /// Small threshold to avoid numerical instability when normalizing vectors.
    let nearZero = 1e-9

    /// Returns the Euclidean length (magnitude) of the vector.
    let inline length v =
        sqrt (v.x * v.x + v.y * v.y + v.z * v.z)

    /// Returns the normalized (unit length) vector, or the original if zero length.
    let inline normalize v =
        let len = length v
        if len <= nearZero then v else v / len

    /// Returns a vector containing components that are the higher of the two vectors'.
    let inline max v1 v2 = { x = max v1.x v2.x; y = max v1.y v2.y; z = max v1.z v2.z }

    /// Returns a vector containing components that are the higher of the two vectors'.
    let inline min v1 v2 = { x = min v1.x v2.x; y = min v1.y v2.y; z = min v1.z v2.z }

    /// Linearly interpolates between two vectors by alpha (0.0 - 1.0).
    let inline lerp (v1: Vector3) (v2: Vector3) alpha = v1 + (v2 - v1) * alpha

    /// Smoothly damps v1 toward v2 using exponential smoothing.
    let damp v1 v2 lambda delta =
        lerp v1 v2 (1.0 - exp (-lambda * delta))

/// Represents a 3-component vector that can have its individual components mutated.
/// Useful when modifying co-ordinates or bounds in an inner loop where performance is critical.
type MutableVector3 = {
    mutable x: float
    mutable y: float
    mutable z: float
} with

    /// Represents the zero vector.
    static member Zero = { x = 0; y = 0; z = 0 }

    /// Converts this mutable vector to an immutable Vector3.
    member this.ToVector3() =
        Vector3.FromComponents(this.x, this.y, this.z)
