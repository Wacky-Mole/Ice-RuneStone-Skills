# IceCaveSkills

IceCaveSkills is a Valheim BepInEx mod that turns Frost Cave murals into one-time discovery rewards.

When a player is the first person in that world to discover an eligible Frost Cave mural, the mural grants `+10` to a random skill. After that, the same mural will not reward anyone else again.

## Features

- Grants a random `+10` skill reward on first mural discovery
- Rewards only the first player to discover each mural
- Prevents duplicate rewards from the same mural in the same world
- Shows an in-game message for both successful rewards and already-claimed murals
- Supports multiplayer version checks through the existing handshake logic

## How it works

The mod hooks Valheim mural-like interactables used in Frost Caves and checks whether the object matches Frost Cave and mural-related naming patterns.

If the mural has not been claimed before:

1. A random valid skill is selected.
2. The player receives `+10` levels in that skill.
3. The mural is marked as claimed for the current world.

If the mural was already claimed, the player gets a short notification instead.

## Notes

- Skill rewards are currently chosen from Valheim's normal skill list, excluding `None` and `All`.
- The reward is capped by the game's normal `100` skill level limit.
- Discovery tracking is stored as world state, so it persists across sessions.
- Detection uses Frost Cave and mural-related heuristics. If a future Valheim update changes prefab names, the detection rules may need to be adjusted.


## Configuration

The mod includes server-synced configuration locking through BepInEx/ServerSync.

- `Lock Configuration`: when enabled, configuration changes are restricted to server admins.

