// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import { downloadJson, firstSelectedFile } from "../../../src/react-components/download";

describe("downloadJson", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("creates a JSON Blob, downloads it with the requested filename, and revokes the URL", () => {
    const createObjectURL = vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:test");
    const revokeObjectURL = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (
      this: HTMLAnchorElement
    ) {
      expect(document.body.contains(this)).toBe(true);
    });

    downloadJson('{"name":"A"}', "family.json");

    expect(createObjectURL).toHaveBeenCalledOnce();
    const blob = createObjectURL.mock.calls[0][0] as Blob & {
      text(): Promise<string>;
    };
    expect(blob).toBeInstanceOf(Blob);
    expect(blob.type).toBe("application/json");
    return blob.text().then((content) => {
      expect(content).toBe('{"name":"A"}');
      expect(click).toHaveBeenCalledOnce();
      const anchor = click.mock.instances[0] as HTMLAnchorElement;
      expect(anchor.download).toBe("family.json");
      expect(anchor.href).toBe("blob:test");
      expect(revokeObjectURL).toHaveBeenCalledWith("blob:test");
      expect(document.body.contains(anchor)).toBe(false);
    });
  });

  it("revokes the object URL after a failed click and still removes the anchor", () => {
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:test");
    const revokeObjectURL = vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {
      throw new Error("click failed");
    });

    expect(() => downloadJson("{}", "family.json")).toThrow("click failed");

    expect(click).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledOnce();
    expect(document.querySelector("a")).toBeNull();
  });
});

describe("firstSelectedFile", () => {
  it("returns the first selected file and resets the input value", () => {
    const input = document.createElement("input");
    input.type = "file";
    const file = new File(["contents"], "family.json", { type: "application/json" });
    Object.defineProperty(input, "value", {
      configurable: true,
      value: "C:\\fakepath\\family.json",
      writable: true,
    });
    Object.defineProperty(input, "files", { configurable: true, value: [file] });

    expect(firstSelectedFile(input)).toBe(file);
    expect(input.value).toBe("");
  });

  it("returns undefined and still resets the input when no file is selected", () => {
    const input = document.createElement("input");
    input.type = "file";
    Object.defineProperty(input, "value", {
      configurable: true,
      value: "C:\\fakepath\\stale.json",
      writable: true,
    });

    expect(firstSelectedFile(input)).toBeUndefined();
    expect(input.value).toBe("");
  });
});
