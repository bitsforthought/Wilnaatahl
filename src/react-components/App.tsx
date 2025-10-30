import React, { useEffect, useMemo } from "react";
import { Canvas } from "@react-three/fiber";
import { useActions } from "koota/react";
import { GraphViewFactory } from "../generated/ViewModel/ViewModel";
import Toolbar from "./Toolbar";
import TreeScene from "./TreeScene";
import { eventActions, worldActions } from "../ecs";

export default function App() {
  const familyGraph = useMemo(() => new GraphViewFactory().LoadGraph(), []);
  const { destroyScene, layoutNodes, spawnScene } = useActions(worldActions);

  useEffect(() => {
    spawnScene(familyGraph);
    layoutNodes(familyGraph);
    return () => {
      destroyScene();
    };
  }, [familyGraph, destroyScene, layoutNodes, spawnScene]);

  const { handlePointerMissed } = useActions(eventActions);
  return (
    <div
      className="w-full h-screen"
      style={{
        width: "100vw",
        height: "100vh",
        display: "flex",
        flexDirection: "column",
        justifyContent: "center",
        alignItems: "center",
      }}
    >
      <Toolbar />
      <div style={{ flex: 1, width: "100%", height: "100%" }}>
        <Canvas
          camera={{ position: [0, 0, 8], fov: 50 }}
          shadows
          onPointerMissed={handlePointerMissed}
        >
          <TreeScene />
        </Canvas>
      </div>
    </div>
  );
}
