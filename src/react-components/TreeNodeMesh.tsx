import React from "react";
import { Html } from "@react-three/drei";
import { useThree } from "@react-three/fiber";
import { Entity } from "koota";
import { useActions, useTrait } from "koota/react";
import { eventActions, NodeLabel, PersonRef, Size, useLabelRef, useMeshRef } from "../ecs";
import { nodeLabelText } from "../i18n/format";
import { useLocale } from "../i18n/hooks";

// The native label writer applies the full-canvas translation.
const htmlOrigin: [number, number] = [0, 0];

function calculatePosition(): [number, number] {
  return htmlOrigin;
}

export function TreeNodeMesh({ entity }: { entity: Entity }) {
  // HuwilpGroup guarantees that the traits are present.
  const person = useTrait(entity, PersonRef)!;
  const size = useTrait(entity, Size)!;
  // The presentation-neutral label view is built by the F# side and carried on the
  // NodeLabel trait; this component formats its dates (via Intl) and composes the
  // multi-line text for the active locale, so a locale change re-composes it.
  const labelView = useTrait(entity, NodeLabel);
  const locale = useLocale();
  const label = nodeLabelText(labelView, locale);
  const hasConnectedEventTarget = useThree((state) => state.events.connected != null);
  const ref = useMeshRef(entity);
  const labelRef = useLabelRef(entity);

  const { handleMeshClick } = useActions(eventActions);

  return (
    <>
      <mesh onClick={handleMeshClick(entity)} castShadow receiveShadow ref={ref}>
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
        {/*
          R3F connects its event target after the first scene render. Drei's
          Html captures that target during its layout effect, so wait for the
          stable target before creating the portal.
        */}
        {label && hasConnectedEventTarget && (
          <Html calculatePosition={calculatePosition} style={{ pointerEvents: "none" }}>
            <div className="node-label" data-person-id={person.Id} ref={labelRef}>
              {label}
            </div>
          </Html>
        )}
      </mesh>
    </>
  );
}
