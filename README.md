# Stick Fighter

A real-time 2D fighting game in Godot 4 (C#), in the spirit of Street Fighter 4, with stick figures in the style of *Your Only Move Is HUSTLE*.

## Play the latest Mac build

1. Open the repo's **Actions** tab, pick the latest green **CI** run on `main`, and download **StickFighter-macOS-…** from its Artifacts.
2. Unzip it, then unzip the `StickFighter.zip` inside it to get `Stick Fighter.app`. The download is zipped twice so the app keeps its permissions.
3. The app is ad-hoc signed, not notarized, so macOS blocks it the first time. Right-click → **Open**, or if that isn't offered, go to **System Settings → Privacy & Security** and click **Open Anyway**. From a terminal, `xattr -dr com.apple.quarantine "Stick Fighter.app"` does the same thing.

## Layout

| Path | What lives there |
| --- | --- |
| `src/Sim/` | The simulation: deterministic game logic in plain .NET. No Godot types, no floats, no wall clock. |
| `tests/Sim.Tests/` | xUnit tests for the simulation. They run without Godot. |
| `game/` | Presentation: Godot scenes and nodes that draw the simulation and poll input. |
| `project.godot`, `StickFighter.csproj` | The Godot project. It references `src/Sim`. |

The simulation never depends on the presentation. The rules for the game live in the room's `context/rules`.

## Develop

You need the **.NET build** of [Godot 4.7.2](https://godotengine.org/download) and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet test                                  # simulation tests
dotnet build StickFighter.sln                # everything, including the Godot project
godot --path .                               # run the game
godot --headless --path . -- --smoke         # one-frame headless check, prints "smoke ok"
```

Or open `project.godot` in the Godot editor and press F5.

## Quality gates

Every PR has to pass these:

- **Determinism.** Analyzers in `tools/Determinism.Analyzers` fail the `src/Sim` build on floating-point values (`SF0001`) and on iterating unordered collections (`SF0002`). `src/Sim/BannedSymbols.txt` bans the wall clock, `System.Random`, `Math`, threads and `GetHashCode` (`RS0030`). For a rare justified case, use `#pragma warning disable SF0001 // why`.
- **Types and style.** Nullable is on and warnings are errors, with the .NET analyzers at `latest-recommended` in `src/Sim`. `dotnet format --verify-no-changes` must pass.
- **Allocations.** `tests/Sim.Tests/AllocationTests.cs` fails if a step or save allocates.
- **Perf.** `tools/Bench` measures the p99 of one save + step against **1 ms**, and of a rollback frame (1 load + 9 × save + step) against **10 ms**. It also fails a PR that is more than 20% slower than its base branch, measured on the same runner. Differences under 50 µs are ignored as timer noise. Each mode stops after 15 s, so a slow step fails rather than hangs.

```sh
dotnet format StickFighter.sln --verify-no-changes
dotnet run -c Release --project tools/Bench
```

## CI

`.github/workflows/ci.yml` runs on every pull request and on every push to `main`:

- **Build, lint and tests:** format check, the build with all analyzers, and `dotnet test`.
- **Perf benchmark:** `tools/Bench`, compared against the base branch on PRs.
- **Godot headless smoke test:** imports the project and runs the main scene headless.
- **macOS build** (pushes to `main` only): exports a universal, ad-hoc signed app and attaches it to the run as an artifact.
