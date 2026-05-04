import React, { useEffect, useRef } from "react";
import { Canvas } from "@react-three/fiber";
import { useActions, useHas, useWorld } from "koota/react";
import { FamilyGraph_FamilyGraph as FamilyGraph } from "../generated/Model";
import { Transform_toJson } from "../generated/Persistence/Transform";
import Toolbar from "./Toolbar";
import TreeScene from "./TreeScene";
import { eventActions, OpenFileRequested, SaveRequested, worldActions } from "../ecs";

// Suggested filename for the downloaded JSON. The graph carries no Wilp name to
// derive one from, so this is a static default.
const SAVE_FILENAME = "wilnaatahl.json";

// Extension and MIME are both listed because mobile pickers are inconsistent
// about MIME types.
const FILE_ACCEPT = ".json,application/json";

interface VisualizerProps {
  /** The family graph to render into the current World. */
  graph: FamilyGraph;
  /** Called with the file the user picks via the hidden file input. */
  onFileSelected: (file: File) => void;
}

// Serialize a JSON string to a Blob and trigger a portable download. No native
// save dialog is shown — the browser writes to its download location — which
// keeps this working across all browsers.
function downloadJson(json: string, filename: string) {
  const blob = new Blob([json], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

/**
 * Renders one family graph as an interactive 3D scene with a toolbar, operating
 * on the World provided by context. On mount it spawns the controls and scene
 * into that World; teardown happens when App destroys the World, so there is no
 * per-entity cleanup here.
 *
 * The Open and Save toolbar buttons are ECS `Button` entities whose clicks a F#
 * system turns into `OpenFileRequested` / `SaveRequested` World signals; this
 * component is the bridge that fulfils those signals with browser IO (the OS
 * file picker and a Blob download) and clears them once consumed.
 */
export default function Visualizer({ graph, onFileSelected }: VisualizerProps) {
  const world = useWorld();
  const { layoutNodes, spawnControls, spawnScene } = useActions(worldActions);
  const { handlePointerMissed } = useActions(eventActions);
  const inputRef = useRef<HTMLInputElement>(null);
  const spawned = useRef(false);

  // Spawn once into the current World. A ref guard keeps this idempotent under
  // StrictMode's development effect double-invocation (each World gets its own
  // Visualizer instance via the keyed provider, so the guard is per-World).
  useEffect(() => {
    if (spawned.current) return;
    spawned.current = true;
    spawnControls();
    spawnScene(graph);
    layoutNodes(graph);
  }, [graph, layoutNodes, spawnControls, spawnScene]);

  // Fulfil an Open request by opening the OS file picker. Cleared immediately so
  // a later Open re-triggers even if the picker was cancelled.
  const openRequested = useHas(world, OpenFileRequested);
  useEffect(() => {
    if (!openRequested) return;
    world.remove(OpenFileRequested);
    inputRef.current?.click();
  }, [openRequested, world]);

  // Fulfil a Save request by serializing the current graph and downloading it.
  const saveRequested = useHas(world, SaveRequested);
  useEffect(() => {
    if (!saveRequested) return;
    world.remove(SaveRequested);
    downloadJson(Transform_toJson(graph), SAVE_FILENAME);
  }, [saveRequested, world, graph]);

  const onInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) onFileSelected(file);
    // Reset so re-picking the same file still fires onChange.
    e.target.value = "";
  };

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
      <input
        ref={inputRef}
        type="file"
        accept={FILE_ACCEPT}
        onChange={onInputChange}
        style={{ display: "none" }}
      />
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
