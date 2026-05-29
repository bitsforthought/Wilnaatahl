import React from "react";
import { Html } from "@react-three/drei";
import { Entity } from "koota";
import { useActions, useTrait } from "koota/react";
import { defaultArg } from "../generated/fable_modules/fable-library-ts.5.1.0/Option.js";
import { eventActions, Size, PersonRef, useMeshRef } from "../ecs";

// Scales the HTML label so it tracks the apparent size of the node as the camera
// moves. drei's <Html> handles the per-frame scaling internally by mutating the
// wrapping div's CSS transform — no React state involved, so the label survives
// the remount that selection triggers (HuwilpGroup splits selected vs unselected
// entities into separate <TreeNodeMesh> lists). Tune by eye to taste.
const labelDistanceFactor = 8;

export function TreeNodeMesh({ entity }: { entity: Entity }) {
  // HuwilpGroup guarantees that the traits are present.
  const person = useTrait(entity, PersonRef)!;
  const size = useTrait(entity, Size)!;
  const label = defaultArg(person.Label, undefined);
  const ref = useMeshRef(entity);

  const { handlePointerDown, handleMeshClick } = useActions(eventActions);

  return (
    <>
      <mesh
        onClick={handleMeshClick(entity)}
        onPointerDown={handlePointerDown(entity)}
        castShadow
        receiveShadow
        ref={ref}
      >
        {person.Shape === "cube" ? (
          <boxGeometry args={[size.x, size.y, size.z]} />
        ) : (
          <sphereGeometry args={[Math.min(size.x, size.y, size.z), 16, 16]} />
        )}
        <meshStandardMaterial
          color={"#888888"} // Neutral placeholder; Paint system will overwrite per-Wilp on first frame.
          metalness={0.3} // Slight metallic effect
          roughness={0.3} // Moderate roughness for better light scattering
        />
        {label && (
          <Html position={[0, -0.5, 0]} center distanceFactor={labelDistanceFactor}>
            <div
              style={{
                color: "white",
                textAlign: "center",
                pointerEvents: "none",
                width: "160%",
                marginLeft: "-30%",
              }}
            >
              {label}
            </div>
          </Html>
        )}
      </mesh>
    </>
  );
}
