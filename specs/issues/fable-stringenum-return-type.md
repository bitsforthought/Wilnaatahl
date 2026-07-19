# `StringEnum` values are annotated as `string` in return position under the TypeScript target

> Issue report for Fable maintainers. Tested against Fable 5.1.0.
> Not yet filed upstream — no existing issue covers this (searched the tracker
> including every `TypeScript`-labelled issue). File at
> <https://github.com/fable-compiler/Fable/issues/new/choose>, labels `bug` +
> `TypeScript`.

## Summary

Under `--lang typescript`, a `[<StringEnum>]` union's **type declaration** is emitted
correctly as a string-literal union, but a function or lambda that returns one of its
cases **directly** is annotated with the widened `string` rather than the union type.

When such a value flows into a context typed by the union — a generic call, a field, a
parameter — the emitted TypeScript does not type-check, so `tsc` fails on generated
output that cannot be hand-edited.

## Repro

`Repro.fs`:

```fsharp
module Repro

open Fable.Core

[<StringEnum>]
type Locale = En

type Box<'T> = { Factory: unit -> 'T }

let makeBox<'T> (factory: unit -> 'T) : Box<'T> = { Factory = factory }

// 1. plain
let box1: Box<Locale> = makeBox (fun () -> En)

// 2. inner type annotation on the returned value
let box2: Box<Locale> = makeBox (fun () -> (En: Locale))

// 3. whole-lambda type annotation
let box3: Box<Locale> = makeBox ((fun () -> En): unit -> Locale)

// 4. named function with an explicit return type
let enFactory () : Locale = En
let box4: Box<Locale> = makeBox enFactory

// 5. annotated let inside the lambda
let box5: Box<Locale> = makeBox (fun () -> let v: Locale = En in v)

// 6. direct record construction, no generic inference
let box6: Box<Locale> = { Factory = fun () -> En }

// 7. annotated module-level binding — this one works
let enValue: Locale = En
let box7: Box<Locale> = makeBox (fun () -> enValue)

// 8. unbox — this one works too
let box8: Box<Locale> = makeBox (fun () -> unbox<Locale> En)
```

```
dotnet fable Repro --lang typescript --outDir Repro/out
```

## Actual output

Excerpt of the emitted `Repro.ts`, with Fable's blank lines preserved so the
box-to-box offsets match the `tsc` line numbers below (`box1` is line 24 in the full
file):

```ts
export type Locale = "en";

// … Box$1 class and makeBox function omitted …

export const box1: Box$1<Locale> = makeBox<Locale>((): string => "en");

export const box2: Box$1<Locale> = makeBox<Locale>((): string => "en");

export const box3: Box$1<Locale> = makeBox<Locale>((): string => "en");

export function enFactory(): string {
  return "en";
}

export const box4: Box$1<Locale> = makeBox<Locale>(enFactory);

export const box5: Box$1<Locale> = makeBox<Locale>((): string => "en");

export const box6: Box$1<Locale> = new Box$1((): string => "en");

export const enValue = "en";

export const box7: Box$1<Locale> = makeBox<Locale>((): Locale => enValue);

export const box8: Box$1<Locale> = makeBox<Locale>((): Locale => "en");
```

The type declaration is correct. `box1`–`box6` are annotated `(): string` and fail;
only `box7` and `box8` carry `(): Locale`.

`tsc --noEmit --strict --target ES2020 --module ESNext --moduleResolution bundler --allowImportingTsExtensions`:

```
Repro.ts(24,66): error TS2322: Type 'string' is not assignable to type '"en"'.
Repro.ts(26,66): error TS2322: Type 'string' is not assignable to type '"en"'.
Repro.ts(28,66): error TS2322: Type 'string' is not assignable to type '"en"'.
Repro.ts(34,52): error TS2345: Argument of type '() => string' is not assignable to parameter of type '() => "en"'.
  Type 'string' is not assignable to type '"en"'.
Repro.ts(36,66): error TS2322: Type 'string' is not assignable to type '"en"'.
Repro.ts(38,14): error TS2322: Type 'Box$1<string>' is not assignable to type 'Box$1<"en">'.
  Type 'string' is not assignable to type '"en"'.
```

## Expected

The return annotation should be the declared union type (or omitted, letting TS infer
the literal):

```ts
export const box1: Box$1<Locale> = makeBox<Locale>((): Locale => "en");
export function enFactory(): Locale {
  return "en";
}
```

## Workarounds

Which annotation is emitted depends on the **shape of the returned expression**, not on
any annotation the author writes. Returning the case directly — or via a `let` bound
_inside_ the lambda — emits `string`. Of the variants tested below, the two that emit
the union type both route the case through an indirection: a **module-level** annotated
binding, or an `unbox` cast. Other untested shapes may also work.

| Variant                                                        | Emitted return | `tsc`     |
| -------------------------------------------------------------- | -------------- | --------- |
| `makeBox (fun () -> En)`                                       | `(): string`   | ❌ TS2322 |
| `makeBox (fun () -> (En: Locale))`                             | `(): string`   | ❌ TS2322 |
| `makeBox ((fun () -> En): unit -> Locale)`                     | `(): string`   | ❌ TS2322 |
| `let enFactory () : Locale = En` … `makeBox enFactory`         | `(): string`   | ❌ TS2345 |
| `makeBox (fun () -> let v: Locale = En in v)`                  | `(): string`   | ❌ TS2322 |
| `{ Factory = fun () -> En }` (direct record construction)      | `(): string`   | ❌ TS2322 |
| **`let enValue: Locale = En` … `makeBox (fun () -> enValue)`** | `(): Locale`   | ✅        |
| **`makeBox (fun () -> unbox<Locale> En)`**                     | `(): Locale`   | ✅        |

So the bug is avoidable, but the known escapes are unobvious indirections that have to
be explained wherever they appear. Everything a developer reaches for first —
annotating the lambda, annotating the returned expression inline, or giving the
enclosing function an explicit return type — still emits `string`.

## Probable root cause

`src/Fable.Transforms/FSharp2Fable.Util.fs` preserves the entity type under the
TypeScript target:

```fsharp
| _ when hasAttrib Atts.stringEnum tdef.Attributes && Compiler.Language <> TypeScript -> Fable.String
| _ -> Fable.DeclaredType(FsEnt.Ref tdef, genArgs)
```

so the declaration is emitted correctly via `transformStringEnumDeclaration` →
`makeStringEnumTypeAnnotation`. But a StringEnum **case value** (`En`) compiles to an
expression whose `Type` is `Fable.String` — it is a bare string literal at runtime —
and a lambda/function return annotation is derived from that body type via
`makeTypeAnnotation` in `Fable2Babel.fs`. That path emits `StringTypeAnnotation`
instead of routing back through the declared type; the two paths are disconnected.

The variant results corroborate this, including the ones that fail. A **local** `let`
is inlined away — `box5` emits `(): string => "en"`, the binding and its annotation
gone — so the return-type path sees a `Fable.String` literal again. A **module-level**
binding survives as a named reference (`box7` emits `(): Locale => enValue`) whose type
is `Fable.DeclaredType(Locale)`, and `unbox<Locale>` types the cast expression
directly; in both of those cases `makeTypeAnnotation` then emits `Locale`.

## Environment

| Component        | Version             |
| ---------------- | ------------------- |
| Fable            | 5.1.0               |
| Fable.Core       | 5.0.0               |
| fable-library-ts | 5.1.0               |
| Target           | `--lang typescript` |
| TypeScript       | 5.7.2               |
| .NET SDK         | 10.0                |
| OS               | Windows             |

## Impact here

Hit while decorating the localization catalog (`ViewModel/Localization.fs`). The
generated ECS trait line

```ts
export const CurrentLocale: IMutableValueTrait$2<Locale, Locale> = Trait_refTrait<Locale>(
  (): string => "en"
);
```

breaks `npm run build`, and being generated output it cannot be patched.

`Locale` is therefore left undecorated, and `UiText` — never used in a
factory-return position — keeps `[<StringEnum>]`. The annotated-binding workaround
above would let `Locale` keep the attribute too, at the cost of an indirection whose
only purpose is to dodge a compiler bug; that trade is worth revisiting if this is
fixed upstream.
