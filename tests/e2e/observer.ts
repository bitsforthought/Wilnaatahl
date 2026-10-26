import { expect, type Page } from "@playwright/test";
import type { ObserverSnapshot } from "../../src/testing/observer";

export async function readObserver(page: Page): Promise<ObserverSnapshot> {
  return page.evaluate(() => {
    const observer = globalThis.__WILNAATAHL_E2E_OBSERVER__;
    if (!observer) throw new Error("E2E observer is not registered.");
    return observer.snapshot();
  });
}

export async function waitForStableObserver(page: Page): Promise<ObserverSnapshot> {
  await expect
    .poll(() => page.evaluate(() => globalThis.__WILNAATAHL_E2E_OBSERVER__ !== undefined), {
      timeout: 5000,
      intervals: [50, 100, 200],
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
      { timeout: 5000, intervals: [50, 100, 200] }
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
        const sameRenderedState =
          JSON.stringify(latest.nodes) === JSON.stringify(previous.nodes) &&
          JSON.stringify(latest.camera) === JSON.stringify(previous.camera);
        stableSamples =
          latest.frame > previous.frame &&
          !latest.dragging &&
          latest.mode !== "unknown" &&
          latest.nodes.length > 0 &&
          sameRenderedState
            ? stableSamples + 1
            : 0;
        previous = latest;
        return stableSamples;
      },
      { timeout: 5000, intervals: [50, 100, 200] }
    )
    .toBeGreaterThanOrEqual(2);

  return latest;
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
      { timeout: 5000, intervals: [50, 100, 200] }
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
      { timeout: 5000, intervals: [50, 100, 200] }
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
