# Tweakr (base)

Windows tweak toolkit where every tweak is a reversible, previewable card.

## Setup (Windows, .NET 8 SDK)

```
cd Tweakr
dotnet new sln -n Tweakr
dotnet sln add Tweakr.Core Tweakr.Tweaks Tweakr.App
dotnet run --project Tweakr.App     # run from an elevated terminal, or accept the UAC prompt
```

## How it fits together
- `Tweakr.Core`: `ITweak`, operations (`registry`, `service`), `JsonTweak`, `TweakLoader`, `TweakEngine`, history log, restore point.
- `Tweakr.Tweaks/Definitions/*.json`: declarative tweaks (a file can be one tweak or an array = a pack). Embedded as resources.
- `Tweakr.App`: WPF shell, MVVM, card UI. Toggle cards, queue changes, Preview / Apply / Discard.

## Adding a tweak
Drop a JSON object or array into `Tweakr.Tweaks/Definitions/`. Registry `on`/`off` accept a value, `"<delete>"` (remove value) or `"<delete-key>"` (remove key).
To add a new operation type: implement `IOperation`, register it in `OperationFactory`.

## Where state lives
`%LocalAppData%\Tweakr\history.json` stores every change with the old values needed to undo it.

## Not built yet
History page UI, preset profiles, live registry watcher ("reverted by system"), benchmarks, .reg exports,
remaining operation types (scheduledTask, powercfg, file, powershell, appx), Choice/Slider/Action cards, elevated helper split, staggered page transitions.
