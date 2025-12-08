# Unity Roll-a-Ball Template

This template seeds a Roll-a-Ball style project that demonstrates:

- A Unity-alike game loop (`Game/GameLoop.cs`) that exercises the Unity alias layer.
- Basic UI state tracking (`UI/Scoreboard.cs`).
- Placeholder runtime specs (`tests/runtime/Template/Bootstrap.spec.cs`) to wire the verification flow.
- An `assets/` directory with starter room metadata and UI copy that is mirrored into the generated `out/assets/` folder during scaffolding.

## Gameplay overview

- `GameLoop.PlayRoundAsync` drives a Promise-based win condition. Each tick nudges the `PlayerController`, waits on `Roblox.Promise.Delay`, and resolves with the winning player once the scoreboard hits the configured target.
- Manual loops still work—call `Start()`/`Tick()`/`Stop()` directly in specs or integration code when you want deterministic control for tests.
- The scoreboard retains collected totals between ticks; call `Stop(clearScores: true)` if you need to wipe it explicitly.

## Customisation quick hits

1. Update `assets/TextLabels/ScoreboardLabel.txt` to swap the on-screen branding or copy.
2. Tune input behaviour inside `Game/PlayerController.cs`—for example, drive movement from Unity alias input helpers instead of the built-in timer.
3. Moving `src/` or `out/`? Update `roblox-cs.yml` first; the CLI verification step will fail fast (and note the error in `roblox-cs.verification.json`) if the paths go stale.

Every file contains `__PROJECT_NAME__` and `__PROJECT_NAMESPACE__` tokens that the `roblox-cs new` command replaces based on the CLI flags you provide.
