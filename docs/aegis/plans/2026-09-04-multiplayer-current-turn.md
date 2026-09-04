# Multiplayer Current-Turn Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use aegis:executing-plans to implement this plan task-by-task.

**Goal:** Enable automatic multiplayer detection while limiting the solver to the local player's current turn.

**Architecture:** Runtime derives a current-turn-only policy from the existing `NetService.Type.IsMultiplayer()` signal. Search honors that policy by stopping after the first completed turn layer and by omitting cross-turn continuation state. Existing singleplayer simulation ownership and native multiplayer action synchronization remain unchanged.

**Tech Stack:** C# / .NET 9 / Godot, embedded CombatSolver search engine, existing unattended test protocol.

**Baseline / Authority Refs:** `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/TEST_MATRIX.md`, `src/Runtime/SolverController.cs`, `src/Search/SearchPolicySnapshot.cs`, `src/Search/CombatBeamSolver.Phases.cs`.

**Compatibility Boundary:** Singleplayer policy and long-horizon search remain unchanged. Multiplayer must never auto-plan another player's turn, must not claim cross-turn prediction, and must fail explicitly if the existing simulation root cannot represent the multiplayer roster.

**Verification:** Release build, architecture boundary script, and an unattended current-turn representative scenario or explicit runtime blocker evidence.

---

### Task 1: Add policy boundary

**Files:**
- Modify: `src/Search/SearchPolicySnapshot.cs`
- Modify: `src/Runtime/SolverController.cs`

**Why this task exists:** Make multiplayer current-turn scope explicit and immutable at request creation.

**Impact / Compatibility:** The new flag defaults to false for all existing callers; Runtime sets it only when the current run is multiplayer.

**Verification:** Compile-time construction coverage via Release build and existing policy snapshot fixture.

- [ ] Add `bool CurrentTurnOnly` to `SearchPolicySnapshot` and populate it from `IsMultiplayerSession` in `CaptureSearchPolicy`.
- [ ] Keep existing multiplayer guards for non-local/unsupported lifecycle paths; remove only the guard that prevents a local player turn search, after the simulator boundary is resolved.

### Task 2: Stop search at the current-turn boundary

**Files:**
- Modify: `src/Search/CombatBeamSolver.Phases.cs`

**Why this task exists:** Prevent multiplayer searches from expanding or publishing future turns.

**Impact / Compatibility:** Singleplayer loop condition remains the existing time/node policy. Current-turn mode stops after `searchedTurnLayers >= 1`, returns no continuations, and keeps the existing final current-turn ranking.

**Verification:** Unattended result asserts `SearchedTurns == 1`, `Continuations.Count == 0`, and all actions have the start turn.

- [ ] Use a local horizon predicate in the expansion loop.
- [ ] Set `continuations` to an empty list for current-turn mode.
- [ ] Ensure the boundary is reported as `TurnLimit` when the first turn layer completes without another terminal boundary.

### Task 3: Runtime lifecycle and documentation

**Files:**
- Modify: `src/Runtime/Entry.cs`
- Modify: `src/Runtime/PlayerTurnSetupPatches.cs`
- Modify: `docs/TEST_MATRIX.md`
- Modify: `docs/DEVELOPMENT_NOTES.md`

**Why this task exists:** Allow only the local player's multiplayer turn to request the bounded search and document the changed support boundary.

**Impact / Compatibility:** Other-player turns and multiplayer-specific cards/choices remain unsupported; no deployment protocol changes are introduced.

**Verification:** Release build, boundary script, and targeted unattended fixture if the root simulation accepts the multiplayer roster.

- [ ] Remove the unconditional multiplayer early returns only where local-player turn ownership is already checked.
- [ ] Preserve explicit failure for multiplayer roots with more than one simulated player.
- [ ] Update the matrix and development notes with the current-turn-only limitation and evidence status.

### Task 4: Verification and commit

- [ ] Run `dotnet build CombatSolver.csproj -c Release`.
- [ ] Run `pwsh -NoProfile -File tools/verify-refactor-boundaries.ps1`.
- [ ] Run one targeted unattended current-turn scenario; if blocked by the single-player root assertion, record that blocker and do not claim multiplayer runtime support.
- [ ] Commit only the task files.
