import React from "react";
import { DragControls } from "@react-three/drei";
import { Not } from "koota";
import { useActions, useQuery } from "koota/react";
import { eventActions, Elbow, Hidden, Line, Size, PersonRef, Position, Selected } from "../ecs";
import { ElbowSphereMesh } from "./ElbowSphereMesh";
import { LineMesh } from "./LineMesh";
import { TreeNodeMesh } from "./TreeNodeMesh";

export function HuwilpGroup() {
  const staticEntities = useQuery(Size, PersonRef, Not(Selected, Hidden));
  const draggableEntities = useQuery(Size, PersonRef, Selected, Not(Hidden));
  const lines = useQuery(Line, Not(Hidden));
  const elbows = useQuery(Elbow, Position, Not(Hidden));
  const { handleDrag, handleDragEnd, handleDragStart } = useActions(eventActions);

  // We have two groups below to control the positioning of the wilp in the scene.
  // The outer group defines a central vertical axis that should be shared by all huwilp,
  // and sets the rotation of this wilp around that axis. The inner group sets the position
  // of the wilp relative to that axis.
  //
  // This is the generalized formula for the radius of an inscribed circle of an n-sided
  // regular polygon.
  const faceWidth = 7;
  const huwilpCount = 3;
  const faceDepth = faceWidth / (2 * Math.tan(Math.PI / huwilpCount));

  // If you want to kick the tires on rotation, add this back:
  /*
      <group rotation-y={(Math.PI * 2) / 3}>
        <group position={[0, 1, faceDepth]}>
          <mesh castShadow receiveShadow>
            <sphereGeometry args={[0.4, 16, 16]} />
            <meshStandardMaterial
              color={"#FF0000"} // Default to red; Paint system will update it as needed.
              metalness={0.3} // Slight metallic effect
              roughness={0.3} // Moderate roughness for better light scattering
            />
          </mesh>
        </group>
      </group>
      <group rotation-y={((Math.PI * 2) / 3) * 2}>
        <group position={[0, 1, faceDepth]}>
          <mesh castShadow receiveShadow>
            <sphereGeometry args={[0.4, 16, 16]} />
            <meshStandardMaterial
              color={"#FF0000"} // Default to red; Paint system will update it as needed.
              metalness={0.3} // Slight metallic effect
              roughness={0.3} // Moderate roughness for better light scattering
            />
          </mesh>
        </group>
      </group>
  */
  return (
    <group rotation-y={0}>
      <group rotation-y={0}>
        <group position={[0, 1, faceDepth]}>
          <DragControls
            autoTransform={false}
            axisLock="z"
            onDrag={handleDrag}
            onDragEnd={handleDragEnd}
            onDragStart={handleDragStart}
          >
            {draggableEntities.map((entity) => (
              <TreeNodeMesh entity={entity} key={entity.id()} />
            ))}
          </DragControls>
          {staticEntities.map((entity) => (
            <TreeNodeMesh entity={entity} key={entity.id()} />
          ))}
          {lines.map((entity) => (
            <LineMesh entity={entity} key={entity.id()} />
          ))}
          {elbows.map((entity) => (
            <ElbowSphereMesh entity={entity} key={entity.id()} />
          ))}
        </group>
      </group>
    </group>
  );
}
