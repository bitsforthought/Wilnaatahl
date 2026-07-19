import React from "react";
import { Html } from "@react-three/drei";
import { Entity } from "koota";
import { useActions, useTrait } from "koota/react";
import { eventActions, NodeLabel, Size, PersonRef, useMeshRef } from "../ecs";
import { composeNodeLabel } from "../i18n/format";
import { useLocale } from "../i18n/hooks";

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
  // The presentation-neutral label view is built by the F# side and carried on the
  // NodeLabel trait; this component formats its dates (via Intl) and composes the
  // multi-line text for the active locale, so a locale change re-composes it.
  const labelView = useTrait(entity, NodeLabel);
  const locale = useLocale();
  const label = labelView ? composeNodeLabel(labelView, locale) : "";
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
                whiteSpace: "pre-line",
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
