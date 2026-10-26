import type { CDPSession, Page } from "@playwright/test";

export interface TouchContact {
  id: number;
  x: number;
  y: number;
}

export interface TouchInput {
  startContact(contact: TouchContact): Promise<void>;
  moveContacts(contacts: readonly TouchContact[]): Promise<void>;
  endGesture(): Promise<void>;
  cancel(): Promise<void>;
  close(): Promise<void>;
}

export interface PointerEventRecord {
  type: string;
  pointerId: number;
  pointerType: string;
  isTrusted: boolean;
  targetTag: string;
}

type TouchEventType = "touchStart" | "touchMove" | "touchEnd" | "touchCancel";

export async function installPointerEventProbe(page: Page): Promise<void> {
  await page.evaluate(() => {
    const root = document.documentElement;
    root.dataset.e2ePointerEvents = "[]";
    for (const type of ["pointerdown", "pointermove", "pointerup", "pointercancel"]) {
      document.addEventListener(
        type,
        (event) => {
          if (!(event instanceof PointerEvent)) return;
          const targetTag = event.target instanceof Element ? event.target.tagName : "unknown";
          const record: PointerEventRecord = {
            type: event.type,
            pointerId: event.pointerId,
            pointerType: event.pointerType,
            isTrusted: event.isTrusted,
            targetTag,
          };
          const records = JSON.parse(root.dataset.e2ePointerEvents ?? "[]") as PointerEventRecord[];
          records.push(record);
          root.dataset.e2ePointerEvents = JSON.stringify(records);
        },
        true
      );
    }
  });
}

export async function readPointerEvents(page: Page): Promise<PointerEventRecord[]> {
  return page.evaluate(() => {
    const serialized = document.documentElement.dataset.e2ePointerEvents;
    if (serialized === undefined) throw new Error("Pointer event probe is not installed.");
    return JSON.parse(serialized) as PointerEventRecord[];
  });
}

export async function createTouchInput(page: Page): Promise<TouchInput> {
  const session: CDPSession = await page.context().newCDPSession(page);
  await session.send("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 2 });
  const activeContacts = new Map<number, TouchContact>();

  async function dispatch(
    type: TouchEventType,
    contacts: readonly TouchContact[] = Array.from(activeContacts.values())
  ) {
    await session.send("Input.dispatchTouchEvent", {
      type,
      touchPoints: [...contacts],
    });
  }

  async function cancel() {
    if (activeContacts.size === 0) return;
    activeContacts.clear();
    await dispatch("touchCancel", []);
  }

  return {
    async startContact(contact) {
      if (activeContacts.has(contact.id)) {
        throw new Error(`Touch contact ${contact.id} is already active.`);
      }
      if (activeContacts.size >= 2) {
        throw new Error("Touch input supports at most two active contacts.");
      }
      activeContacts.set(contact.id, contact);
      await dispatch("touchStart");
    },
    async moveContacts(contacts) {
      const updatedIds = new Set<number>();
      for (const contact of contacts) {
        if (!activeContacts.has(contact.id)) {
          throw new Error(`Touch contact ${contact.id} is not active.`);
        }
        if (updatedIds.has(contact.id)) {
          throw new Error(`Touch contact ${contact.id} was updated more than once.`);
        }
        updatedIds.add(contact.id);
        activeContacts.set(contact.id, contact);
      }
      if (updatedIds.size === 0) throw new Error("Touch movement requires an active contact.");
      await dispatch("touchMove");
    },
    async endGesture() {
      if (activeContacts.size === 0) return;
      activeContacts.clear();
      await dispatch("touchEnd", []);
    },
    cancel,
    async close() {
      await cancel();
      await session.detach();
    },
  };
}
