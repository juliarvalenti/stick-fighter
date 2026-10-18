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
| `tests/Presentation.Tests/` | xUnit tests for Godot-free presentation code in `game/` (the camera math), linked in by file. |
| `game/` | Presentation: Godot scenes and nodes that draw the simulation and poll input. |
| `project.godot`, `StickFighter.csproj` | The Godot project. It references `src/Sim`. |
| `data/characters/` | Character data: stats, skeleton, stance poses (`<name>.json`) and compiled moves (`<name>.moves.json`, generated), as integer JSON. Embedded in the Sim assembly and validated at load; bad data fails naming the move and key. |
| `data/moves/` | Move scripts (`<name>.scripts.json`) and the key-pose library. `tools/MoveCompiler` compiles them into `data/characters/<name>.moves.json`; edit the scripts, never the compiled file. |

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

- **Determinism.** Analyzers in `tools/Determinism.Analyzers` fail the `src/Sim` build on floating-point values (`SF0001`) and on iterating unordered collections (`SF0002`). `src/Sim/BannedSymbols.txt` bans the wall clock, `System.Random`, `Math`, threads, `GetHashCode`, and the hash-based collections (`Dictionary`, `HashSet` and their interfaces), whose order is undefined (`RS0030`). For a rare justified case, use `#pragma warning disable SF0001 // why`.
- **Types and style.** Nullable is on and warnings are errors, with the .NET analyzers at `latest-recommended` in `src/Sim`. `dotnet format --verify-no-changes` must pass.
- **Allocations.** `tests/Sim.Tests/AllocationTests.cs` fails if a step or save allocates.
- **Perf.** `tools/Bench` measures the p99 of one save + step against **1 ms**, and of a rollback frame (1 load + 9 × save + step) against **10 ms**. On a PR that touches `src/Sim`, `data` or `tools/Bench`, CI also benchmarks the base branch with the same harness, in five rounds alternating with this change's on the same runner, and fails if the median of either mode is more than 20% slower (and by more than 0.1 µs). Medians, because a p99 near the noise floor flips on a single slow frame. Tiered compilation is off in the bench, so it times optimized code from the first frame. Each mode stops after 15 s, so a slow step fails rather than hangs.

- **Replays.** Every `replays/*.sfr` must replay to the same checksum on every frame. `ReplayRunner check` prints the first diverging frame. In the game, **F9** saves the current match to `user://replays/`; copy it into `replays/` to turn a bug into a test. After a deliberate behavior change, `ReplayRunner rerecord replays` rewrites the checksums. Review that diff. A replay records the stage it was played on, and `ReplayRunner script <file> <name>` writes one of the scripted matches (`stage-apart`, `stage-walls`, `stage-corner`).
- **Scenarios and golden images.** Each scenario in `game/Scenarios.cs` is a scripted match. The game renders its own viewport to PNG at chosen frames and compares each capture with `tests/golden/`. CI uploads every capture, plus a `.diff.png` on a mismatch, as the `scenarios` artifact. Reviewers look at those instead of opening a build. The `pacing` scenario fails if any jittered 60 Hz frame runs other than one tick. Every visible feature adds a scenario.

- **Moves.** `tools/MoveCompiler --check` fails if the compiled moves don't match their scripts, or if any frame breaks the body checks (elbows or knees bending backwards, no planted foot, a foot through the floor, a shin through the torso). Each move has a `sheet-<input>` scenario: every frame side by side with boxes, compared with its golden image.

```sh
dotnet run --project tools/MoveCompiler       # after editing data/moves/: recompile, then commit both
dotnet format StickFighter.sln --verify-no-changes
dotnet run -c Release --project tools/Bench
dotnet run --project tools/ReplayRunner -- check replays
godot --path . --rendering-method gl_compatibility --fixed-fps 60 -- --scenario walk-in --out /tmp/shots
tools/update-goldens.sh <ci-run-id>   # accept CI's captures as the new goldens after an intended change
```

Goldens come from CI's software renderer, not a local machine, since GPUs anti-alias edges slightly differently.

## CI

`.github/workflows/ci.yml` runs on every pull request and on every push to `main`:

- **Build, lint and tests:** format check, the build with all analyzers, `dotnet test` and the replays.
- **Perf benchmark:** `tools/Bench` within budget, and on PRs that change the simulation, compared against the base branch in alternating rounds.
- **Godot headless smoke test:** imports the project and runs the main scene headless.
- **Scenarios and golden images:** renders every scenario under `xvfb` and compares with the goldens.
- **macOS build** (pushes to `main`, or run by hand): exports a universal, ad-hoc signed app, checks that it launches, and attaches it to the run as an artifact.
