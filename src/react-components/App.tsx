import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createWorld, World } from "koota";
import { WorldProvider } from "koota/react";
import { FamilyGraph_FamilyGraph as FamilyGraph } from "../generated/Model";
import {
  ImportError_$union as ImportError,
  ImportWarning_$union as ImportWarning,
} from "../generated/Persistence/Transform";
import {
  ImportService_importJsonText,
  ImportService_loadSampleGraph,
  ImportSuccess,
} from "../generated/Persistence/ImportService";
import {
  ImportErrorModule_toMessage,
  ImportWarningModule_summary,
  ImportWarningModule_toMessage,
} from "../generated/ViewModel/ImportMessages";
import { FSharpList } from "../generated/fable_modules/fable-library-ts.5.1.0/List";
import Visualizer from "./Visualizer";

// A single visualization: the graph rendered into its own per-load World, plus a
// monotonic key that identifies the World for React. World ids are recycled by
// Koota on destroy, so a fresh monotonic key — not the World id — is what forces
// <WorldProvider> to remount the subtree for each new World.
type Session = { world: World; graph: FamilyGraph; key: number };

export default function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [error, setError] = useState<string | undefined>(undefined);
  const [warnings, setWarnings] = useState<FSharpList<ImportWarning> | undefined>(undefined);

  // Mirrors the current World so unmount destroys whatever World is live now
  // (an import may have swapped it), rather than a World captured at mount.
  const currentWorld = useRef<World | null>(null);
  const nextKey = useRef(0);

  // Boot straight into the sample visualization: create the initial World and
  // seed it with the sample graph. Destroying that World in the matching cleanup
  // keeps this symmetric, so StrictMode's mount/unmount/remount neither leaks a
  // World nor double-spawns into the live one. Uses a layout effect so the
  // initial null render is never painted.
  useLayoutEffect(() => {
    const world = createWorld();
    currentWorld.current = world;
    setSession({ world, graph: ImportService_loadSampleGraph(), key: nextKey.current++ });
    return () => {
      currentWorld.current?.destroy();
      currentWorld.current = null;
    };
  }, []);

  // Load a file into a brand-new World, discarding the previous one. A parse
  // error leaves the current visualization untouched behind an error toast.
  const importFile = useCallback(async (file: File) => {
    try {
      const text = await file.text();
      const result = ImportService_importJsonText(text);
      if (result.tag === 0 /* Ok */) {
        const success = result.fields[0] as ImportSuccess;
        // summary is "" exactly when there are no warnings, so it doubles as a
        // non-emptiness test without reaching into Fable's list representation.
        const summary = ImportWarningModule_summary(success.Warnings);

        const next = createWorld();
        currentWorld.current?.destroy();
        currentWorld.current = next;

        setError(undefined);
        setWarnings(summary !== "" ? success.Warnings : undefined);
        setSession({ world: next, graph: success.Graph, key: nextKey.current++ });
      } else {
        setError(ImportErrorModule_toMessage(result.fields[0] as ImportError));
      }
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      setError(`Could not read file: ${message}`);
    }
  }, []);

  if (!session) return null;

  return (
    <>
      <WorldProvider key={session.key} world={session.world}>
        <Visualizer graph={session.graph} onFileSelected={importFile} />
      </WorldProvider>
      {error && <ErrorToast message={error} onDismiss={() => setError(undefined)} />}
      {warnings && <WarningsToast warnings={warnings} onDismiss={() => setWarnings(undefined)} />}
    </>
  );
}

const toastBaseStyle: React.CSSProperties = {
  position: "fixed",
  bottom: "1em",
  right: "1em",
  color: "rgba(255, 240, 240, 0.95)",
  padding: "0.75em 1em",
  borderRadius: "8px",
  maxWidth: "32em",
  boxShadow: "0 2px 12px rgba(0, 0, 0, 0.4)",
  display: "flex",
  alignItems: "center",
  gap: "0.75em",
};

const dismissButtonStyle: React.CSSProperties = {
  background: "transparent",
  border: "none",
  color: "inherit",
  fontSize: "1.6em",
  lineHeight: 1,
  padding: "0 0.25em",
  cursor: "pointer",
};

function ErrorToast({ message, onDismiss }: { message: string; onDismiss: () => void }) {
  return (
    <div
      role="alert"
      style={{ ...toastBaseStyle, background: "#5c1a1a", border: "1px solid #8a2a2a" }}
    >
      <span>{message}</span>
      <button onClick={onDismiss} aria-label="Dismiss error" style={dismissButtonStyle}>
        ×
      </button>
    </div>
  );
}

function WarningsToast({
  warnings,
  onDismiss,
}: {
  warnings: FSharpList<ImportWarning>;
  onDismiss: () => void;
}) {
  const summary = ImportWarningModule_summary(warnings);

  // Log each warning to the dev console once when the toast appears, so users can
  // open dev tools to see exactly which records need fixing in their source data.
  useEffect(() => {
    console.groupCollapsed(`Wilnaatahl import warnings: ${summary}`);
    for (const w of warnings) {
      console.warn(ImportWarningModule_toMessage(w));
    }
    console.groupEnd();
  }, [warnings, summary]);

  return (
    <div
      role="status"
      style={{ ...toastBaseStyle, background: "#5c4a1a", border: "1px solid #8a7a2a" }}
    >
      <span>Imported with warnings: {summary}. See the dev console for details.</span>
      <button onClick={onDismiss} aria-label="Dismiss warnings" style={dismissButtonStyle}>
        ×
      </button>
    </div>
  );
}
