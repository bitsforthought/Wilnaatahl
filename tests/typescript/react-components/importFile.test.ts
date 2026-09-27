import { describe, expect, test, vi } from "vitest";
import { LocaleModule_parse } from "../../../src/generated/ViewModel/Localization";
import { ImportWarningModule_summary } from "../../../src/generated/ViewModel/ImportMessages";
import { importFile, normalizeThrownValue } from "../../../src/react-components/importFile";

const locale = LocaleModule_parse("en-US");

describe("importFile", () => {
  test("returns the parsed graph and warnings when the parser succeeds", async () => {
    const file = new File(
      [
        JSON.stringify({
          people: [{ id: 0, name: "Alice", gender: "F" }],
        }),
      ],
      "family.json"
    );

    const outcome = await importFile(file, locale);

    expect(outcome.kind).toBe("ok");
    if (outcome.kind === "ok") {
      expect(outcome.graph).toBeDefined();
      expect(outcome.warnings).toBeUndefined();
    }
  });

  test("keeps a non-empty warning list", async () => {
    const file = new File(
      [
        JSON.stringify({
          people: [
            { id: 0, name: "Alice", gender: "F" },
            { id: 1, name: "Carol", gender: "F", parents: 999 },
          ],
        }),
      ],
      "family.json"
    );

    const outcome = await importFile(file, locale);

    expect(outcome.kind).toBe("ok");
    if (outcome.kind === "ok") {
      expect(outcome.warnings).toBeDefined();
      const warnings = Array.from(outcome.warnings ?? []);
      expect(warnings).toHaveLength(1);
      expect(ImportWarningModule_summary(locale, outcome.warnings!)).toBe(
        "1 unresolved parent couple"
      );
    }
  });

  test("returns the parser's localized error message", async () => {
    const file = new File(['{ "people": [] }'], "family.json");

    const outcome = await importFile(file, locale);

    expect(outcome).toEqual({
      kind: "error",
      message: "The file contains no people.",
    });
  });

  test.each([
    [new Error("disk offline"), "disk offline"],
    ["permission denied", "permission denied"],
    [{ code: "EIO" }, '{"code":"EIO"}'],
  ])("normalizes thrown value %j", (thrown, expected) => {
    expect(normalizeThrownValue(thrown)).toBe(expected);
  });

  test("falls back when a thrown object cannot be serialized", () => {
    const thrown: { self?: unknown } = {};
    thrown.self = thrown;

    expect(normalizeThrownValue(thrown)).toBe("[object Object]");
  });

  test("serializes an object whose string conversion throws", () => {
    const thrown = {
      code: "EIO",
      toString: () => {
        throw new Error("string conversion failed");
      },
    };

    expect(normalizeThrownValue(thrown)).toBe('{"code":"EIO"}');
  });

  test("reads a non-enumerable message from an error-like object", () => {
    const thrown = Object.defineProperty({}, "message", { value: "foreign error" });

    expect(normalizeThrownValue(thrown)).toBe("foreign error");
  });

  test("falls back when object serialization returns undefined", () => {
    expect(normalizeThrownValue({ toJSON: () => undefined })).toBe("[object Object]");
  });

  test("uses a generic message when a thrown value cannot be serialized or converted", () => {
    const thrown = Object.create(null) as { self?: unknown };
    thrown.self = thrown;

    expect(normalizeThrownValue(thrown)).toBe("Unknown error");
  });

  test("returns an exact read failure message", async () => {
    const file = {
      text: vi.fn().mockRejectedValue(new Error("disk offline")),
    } as unknown as File;

    await expect(importFile(file, locale)).resolves.toEqual({
      kind: "error",
      message: "Could not read file: disk offline",
    });
  });
});
