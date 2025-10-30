import React from "react";
import { Entity } from "koota";
import { useMeshRef } from "../ecs";

export function ElbowSphereMesh({ entity }: { entity: Entity }) {
  const ref = useMeshRef(entity);

  return (
    <mesh ref={ref}>
      <sphereGeometry args={[0.03, 16, 16]} />
      <meshStandardMaterial color="#AAAAAA" />
    </mesh>
  );
}
