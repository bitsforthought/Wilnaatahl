// Runs the portable F# ECS conformance suite against real Koota.
//
// These tests exist to prove the .NET mock and the Koota wrapper are
// behaviourally equivalent — that equivalence is what lets every other F# test
// run against the mock with confidence. The F# sources live in
// tests/Wilnaatahl.ECS.Tests and are compiled to TypeScript by Fable
// (`npm run test:koota`); this file discovers and drives the compiled output.
//
// It replaces a hand-rolled runner (the former Runner.fs) that printed
// PASS/FAIL lines and returned an exit code. Registering a real Vitest test per
// F# test method gives proper per-test reporting and failure diffs, and — the
// reason it matters here — attributes coverage to src/ecs/koota/kootaWrapper.ts,
// which the compiled FableTestInfra imports directly.
//
// Discovery mirrors what Fable emits for an F# test class: each `[<Fact>]`
// member becomes an exported `ClassName__Method$0020name` function taking the
// instance as its first argument, alongside a `ClassName_$ctor` factory. The
// `__` prefix is what distinguishes test methods from `_$ctor`/`_$reflection`.
// The portable suite intentionally supports only zero-argument `[<Fact>]`
// methods; theory data discovery and property-based execution are not available
// in this Fable/Vitest harness.

import { test } from "vitest";
import * as EntityTests from "../Wilnaatahl.ECS.Tests/out/ECS/EntityTests.ts";
import * as QueryTests from "../Wilnaatahl.ECS.Tests/out/ECS/QueryTests.ts";
import * as RelationTests from "../Wilnaatahl.ECS.Tests/out/ECS/RelationTests.ts";
import * as TrackingTests from "../Wilnaatahl.ECS.Tests/out/ECS/TrackingTests.ts";
import * as TraitTests from "../Wilnaatahl.ECS.Tests/out/ECS/TraitTests.ts";
import * as WorldTests from "../Wilnaatahl.ECS.Tests/out/ECS/WorldTests.ts";

type FableModule = Record<string, unknown>;

/** One compiled F# test class: its module namespace and the class name Fable used to prefix members. */
type TestClass = { className: string; module: FableModule };

const testClasses: TestClass[] = [
  { className: "TraitTests", module: TraitTests },
  { className: "EntityTests", module: EntityTests },
  { className: "QueryTests", module: QueryTests },
  { className: "RelationTests", module: RelationTests },
  { className: "WorldTests", module: WorldTests },
  { className: "TrackingTests", module: TrackingTests },
];

/**
 * Fable encodes characters that are not valid in a JS identifier as `$XXXX`,
 * the Unicode code point in hex — most commonly `$0020` for the spaces in an
 * F# double-backtick member name. Decoding restores the original test name.
 */
function decodeFableName(encoded: string): string {
  return encoded.replace(/\$([0-9A-Fa-f]{4})/g, (_, hex: string) =>
    String.fromCharCode(parseInt(hex, 16))
  );
}

/** The F# test instance, including its generated IDisposable implementation. */
type TestInstance = { Dispose(): void };

function registerTestClass({ className, module }: TestClass): void {
  const memberPrefix = `${className}__`;
  const constructorKey = `${className}_$ctor`;
  const construct = module[constructorKey];

  if (typeof construct !== "function") {
    throw new Error(
      `Expected Fable to export ${constructorKey}. The compiled output shape has changed.`
    );
  }

  const methodKeys = Object.keys(module).filter(
    (key) => key.startsWith(memberPrefix) && typeof module[key] === "function"
  );

  if (methodKeys.length === 0) {
    throw new Error(
      `No test methods found on ${className}. Expected exports prefixed '${memberPrefix}'.`
    );
  }

  for (const key of methodKeys) {
    const method = module[key] as (instance: TestInstance) => void;

    test(decodeFableName(key.slice(memberPrefix.length)), () => {
      // A fresh instance per test, mirroring xUnit's per-test class lifetime.
      const instance = (construct as () => TestInstance)();
      try {
        method(instance);
      } finally {
        instance.Dispose();
      }
    });
  }
}

for (const testClass of testClasses) {
  registerTestClass(testClass);
}
