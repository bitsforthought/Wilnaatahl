import React from "react";
import { Entity } from "koota";
import { useMeshRef } from "../ecs";

export function LineMesh({ entity }: { entity: Entity }) {
  const ref = useMeshRef(entity);

  // Geometry will be set dynamically by the ECS-based rendering system.
  return (
    <mesh ref={ref}>
      <meshStandardMaterial color="#AAAAAA" />
    </mesh>
  );
}
