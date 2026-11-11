import { expect, type Page } from "@playwright/test";
import type { ObserverSnapshot } from "../../src/testing/observer";

const observerWaitTimeoutMs = 5_000;
const observerPollIntervalsMs = [50, 100, 200];

export interface LabelRect {
  left: number;
  top: number;
  width: number;
  height: number;
}

export interface LabelObservation {
  personId: number;
  rect: LabelRect;
  visible: boolean;
}

export interface MinimalLabelScene {
  snapshot: ObserverSnapshot;
  labels: LabelObservation[];
}

export async function readObserver(page: Page): Promise<ObserverSnapshot> {
  return page.evaluate(() => {
    const observer = globalThis.__WILNAATAHL_E2E_OBSERVER__;
    if (!observer) throw new Error("E2E observer is not registered.");
    return observer.snapshot();
  });
}

export async function readMinimalLabelScene(page: Page): Promise<MinimalLabelScene> {
  return page.locator(".node-label").evaluateAll((elements) => {
    const observer = globalThis.__WILNAATAHL_E2E_OBSERVER__;
    if (!observer) throw new Error("E2E observer is not registered.");

    const labels = elements.map((element): LabelObservation => {
      const htmlElement = element as HTMLElement;
      const rect = htmlElement.getBoundingClientRect();
      const styles = getComputedStyle(htmlElement);
      return {
        personId: Number(htmlElement.dataset.personId),
        rect: {
          left: rect.left,
          top: rect.top,
          width: rect.width,
          height: rect.height,
        },
        visible:
          styles.visibility !== "hidden" &&
          styles.display !== "none" &&
          rect.width > 0 &&
          rect.height > 0,
      };
    });

    return { snapshot: observer.snapshot(), labels };
  });
}

export async function waitForStableObserver(
  page: Page,
  options: { requireRenderedState?: boolean; requireProjectedBounds?: boolean } = {}
): Promise<ObserverSnapshot> {
  const requireRenderedState = options.requireRenderedState ?? true;
  const requireProjectedBounds = options.requireProjectedBounds ?? false;
  await expect
    .poll(() => page.evaluate(() => globalThis.__WILNAATAHL_E2E_OBSERVER__ !== undefined), {
      timeout: observerWaitTimeoutMs,
      intervals: observerPollIntervalsMs,
    })
    .toBe(true);

  await expect
    .poll(
      async (): Promise<boolean> => {
        try {
          await readObserver(page);
          return true;
        } catch {
          return false;
        }
      },
      { timeout: observerWaitTimeoutMs, intervals: observerPollIntervalsMs }
    )
    .toBe(true);

  let previous = await readObserver(page);
  let stableSamples = 0;
  let latest = previous;

  await expect
    .poll(
      async () => {
        try {
          latest = await readObserver(page);
        } catch {
          stableSamples = 0;
          return 0;
        }
        if (latest.session !== previous.session) {
          previous = latest;
          stableSamples = 0;
          return 0;
        }
        if (latest.frame === previous.frame) return stableSamples;
        const sameRenderedState = hasSameRenderedState(latest, previous);
        const stableSample = isStableObserverSample(
          latest,
          previous,
          requireRenderedState,
          requireProjectedBounds,
          sameRenderedState
        );
        stableSamples = stableSample ? stableSamples + 1 : 0;
        previous = latest;
        return stableSamples;
      },
      { timeout: observerWaitTimeoutMs, intervals: observerPollIntervalsMs }
    )
    .toBeGreaterThanOrEqual(2);

  return latest;
}

function hasSameRenderedState(current: ObserverSnapshot, previous: ObserverSnapshot): boolean {
  return (
    JSON.stringify(current.nodes) === JSON.stringify(previous.nodes) &&
    JSON.stringify(current.camera) === JSON.stringify(previous.camera) &&
    current.mode === previous.mode &&
    current.dragging === previous.dragging &&
    JSON.stringify(current.selectedPersonIds) === JSON.stringify(previous.selectedPersonIds) &&
    JSON.stringify(current.canvas) === JSON.stringify(previous.canvas)
  );
}

function hasFiniteProjectedBounds(snapshot: ObserverSnapshot): boolean {
  return snapshot.nodes.every((node) => {
    const bounds = node.projectedBounds;
    return (
      bounds !== null && Object.values(bounds).every((coordinate) => Number.isFinite(coordinate))
    );
  });
}

function isStableObserverSample(
  current: ObserverSnapshot,
  previous: ObserverSnapshot,
  requireRenderedState: boolean,
  requireProjectedBounds: boolean,
  sameRenderedState: boolean
): boolean {
  return (
    current.frame > previous.frame &&
    !current.dragging &&
    current.mode !== "unknown" &&
    current.nodes.length > 0 &&
    (!requireProjectedBounds || hasFiniteProjectedBounds(current)) &&
    (!requireRenderedState || sameRenderedState)
  );
}

export async function waitForNewObserverSession(
  page: Page,
  previousSession: number
): Promise<ObserverSnapshot> {
  await expect
    .poll(
      async () => {
        try {
          return (await readObserver(page)).session;
        } catch {
          return previousSession;
        }
      },
      { timeout: observerWaitTimeoutMs, intervals: observerPollIntervalsMs }
    )
    .toBeGreaterThan(previousSession);
  return waitForStableObserver(page);
}

export async function waitForFrameAfter(
  page: Page,
  previous: ObserverSnapshot
): Promise<ObserverSnapshot> {
  let latest = await readObserver(page);
  await expect
    .poll(
      async () => {
        latest = await readObserver(page);
        return (
          latest.session === previous.session && latest.frame > previous.frame && !latest.dragging
        );
      },
      { timeout: observerWaitTimeoutMs, intervals: observerPollIntervalsMs }
    )
    .toBe(true);
  return latest;
}

export async function clickNode(page: Page, personId: number) {
  const snapshot = await waitForStableObserver(page);
  const node = snapshot.nodes.find((candidate) => candidate.personId === personId);
  if (!node) throw new Error(`Observer could not find person ${personId}.`);
  await page.mouse.click(node.cssPosition.x, node.cssPosition.y);
  await waitForStableObserver(page);
}
