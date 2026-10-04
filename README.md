# Merge Performance Fix (RimWorld 1.6)

**Status: experiment, not maintained.** It works, but the measured gain is small, so I no longer use it.

A Harmony patch for the vanilla "merge things" hauling job (`WorkGiver_Merge`, shown as `HaulMerge - Hauling - Core` in Dubs Performance Analyzer).

## What it changes

To merge a partial stack, vanilla goes through every item in the stack's storage group, and does that for each partial stack it considers. With large linked storage that is thousands of items per call.

- `JobOnThing` prefix: checks `ListerMergeables` for another partial stack of the same def in the same storage group first. With none, it returns no job, which is what the full search would return. With one, the vanilla method runs unchanged.
- `PotentialWorkThingsGlobal` / `ShouldSkip` prefixes: partial stacks with no such partner are dropped from the scan list, so they are not reachability-checked first. The list is cached per tick and map.
- Forced orders and pawns outside the player faction always take the vanilla path.

## Measured results

3x speed, Dubs Performance Analyzer, WorkGiver tab:

| HaulMerge | Average per tick | Worst tick |
|---|---|---|
| Before | 0.117 ms | 14.6 ms |
| After | 0.042 to 0.044 ms | 3.5 to 6.1 ms |

- The whole tick costs about 10 ms in the test colony, so the average saving is under 1% and TPS does not change noticeably.
- The main effect is fewer occasional stutters. Each sample had only a few dozen calls and a different game state, so treat the worst-tick numbers as rough.
- When there really is something to merge, the vanilla search still runs, on purpose, so the chosen merge target is unchanged.

## Build

`dotnet build -c Release` in `Source/`, then copy `bin/Release/MergePerformanceFix.dll` to `1.6/Assemblies/`. Paths to RimWorld and Harmony are set in the `.csproj`.
