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

- **Replays.** Every `replays/*.sfr` must replay the same. A replay stores its inputs, a **behavior checkpoint** every 60 frames and on the last frame (each fighter's position, facing, action, action frame and health, and the match's phase, round, timer and wins, as readable text), and one checksum of the whole state at the end. `ReplayRunner check` names the first checkpoint whose behavior differs and how (`P2 health: 880 vs 860`), or says only the final state differs. To find the exact frame, `ReplayRunner trace <file> <from> <to>` prints every frame. Run it on both builds and diff the output.
  - After a deliberate change, `ReplayRunner rerecord replays` rewrites them and prints one line per replay: *unchanged*, *encoding changed, behavior identical*, or *behavior changed at frame N* with what changed. **Paste that summary into the PR.** It's what a reviewer reads; the `.sfr` files are marked generated, so GitHub collapses them. A new state field changes one line per replay, the final checksum.
  - In the game, **F9** saves the current match to `user://replays/`; copy it into `replays/` to turn a bug into a test.
  - A replay records the stage it was played on and the match mode: `training` has no intro, timer or rounds, and `versus` plays the whole match flow. `ReplayRunner script <file> <name>` writes one of the scripted matches (`stage-apart`, `stage-walls`, `stage-corner` and the rest in `Scripts`). Scripts named `match-…` are versus matches, played round by round against the live state until the match is decided (`match-ko-ko`, `match-time-over`, `match-double-ko`, `match-draw`, `match-final-round`).
- **Rollback (SyncTest).** GGPO's SyncTest mode, offline: `ReplayRunner synctest replays` plays every replay and, after every frame, rolls back 1 to 8 frames and resimulates. It does this twice: on the same inputs, and after a misprediction (each player's last input held, then corrected, as an online session does). Every resimulated frame must reproduce its checksum. It catches state the step reads but `GameState` doesn't hold, such as a static, a cache, or anything a load doesn't restore, in the PR that adds it rather than online. Run it on one replay with `ReplayRunner synctest replays/hit.sfr`. A failure says where, and then what. Here a step bumped P2's hitstun from a static counter:

  ```
  FAIL replays/hit.sfr  frame 9 differs after rolling back 4 frames: expected e528b4b49fc1fa65, got a8184677625171aa
    Fighters[1].Hitstun: 0 vs 1
       inputs on that frame: p1 Right, p2 None
  ```

  The frame is the first resimulated frame that came out different, and the depth is how far back the rollback went. "from a misprediction" means only a wrong guess followed by a correction shows it, so look for something the predicted frames left behind. Each indented line is a `GameState` field: the first-run value, then the resimulated one. A field that differs points at the code that writes it. If nothing changed in `GameState` itself but the step still came out different, the step read something outside it.
- **Scenarios and golden images.** Each scenario in `game/Scenarios.cs` is a scripted match. The game renders its own viewport to PNG at chosen frames and compares each capture with `tests/golden/`. CI uploads every capture, plus a `.diff.png` on a mismatch, as the `scenarios` artifact. Reviewers look at those instead of opening a build. The `pacing` scenario fails if any jittered 60 Hz frame runs other than one tick. Every visible feature adds a scenario.

- **Moves.** `tools/MoveCompiler --check` fails if the compiled moves don't match their scripts, or if any frame breaks the body checks (elbows or knees bending backwards, no planted foot, a foot through the floor, a shin through the torso). Each move has a `sheet-<input>` scenario: every frame side by side with boxes, compared with its golden image.

```sh
dotnet run --project tools/MoveCompiler       # after editing data/moves/: recompile, then commit both
dotnet format StickFighter.sln --verify-no-changes
dotnet run -c Release --project tools/Bench
dotnet run --project tools/ReplayRunner -- check replays
dotnet run --project tools/ReplayRunner -- synctest replays
godot --path . --rendering-method gl_compatibility --fixed-fps 60 -- --scenario walk-in --out /tmp/shots
tools/update-goldens.sh <ci-run-id>   # accept CI's captures as the new goldens after an intended change
```

Goldens come from CI's software renderer, not a local machine, since GPUs anti-alias edges slightly differently.

## CI

`.github/workflows/ci.yml` runs on every pull request and on every push to `main`:

- **Build, lint and tests:** format check, the build with all analyzers, `dotnet test`, the replays, and SyncTest over the replays.
- **Perf benchmark:** `tools/Bench` within budget, and on PRs that change the simulation, compared against the base branch in alternating rounds.
- **Godot headless smoke test:** imports the project and runs the main scene headless.
- **Scenarios and golden images:** renders every scenario under `xvfb` and compares with the goldens.
- **macOS build** (pushes to `main`, or run by hand): exports a universal, ad-hoc signed app, checks that it launches, and attaches it to the run as an artifact.
