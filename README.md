# Ice&RuneStone Skills

Ice&RuneStone Skills is a Valheim BepInEx mod that turns Frost Cave murals, cave paintings, and regular runestones into one-time discovery rewards.

When a player is the first person in that world to discover an eligible Frost Cave mural or regular runestone, it grants a configurable reward to a random skill. After that, the same object will not reward anyone else again.

## Features

- Grants configurable random skill rewards from Frost Cave cave paintings and regular runestones
- Uses separate configuration for Frost Cave cave paintings and regular runestones
- Rewards only the first player to discover each eligible object
- Prevents duplicate rewards from the same mural or runestone in the same world
- Shows an in-game message for both successful rewards and already-claimed murals
- Supports Frost Cave cave painting prefabs that use hover text instead of interact behavior
- Supports flat or percentage-based skill rewards for each reward source
- Can disable cave painting rewards or regular runestone rewards through config
- Excludes Vegvisirs and boss runestones from rewards
- Supports multiplayer version checks through the existing handshake logic

## How it works

The mod monitors eligible Frost Cave mural and cave painting hover/interact targets, plus regular RuneStone interactions, and checks whether the object matches the appropriate reward rules.

If the mural or runestone has not been claimed before:

1. A random valid skill is selected.
2. The player receives the configured reward in that skill.
3. The object is marked as claimed on its ZDO for the current world.

If the object was already claimed, the player gets a short notification instead.

## Notes

- Skill rewards are currently chosen from Valheim's normal skill list, excluding `None` and `All`.
- The reward is capped by the game's normal `100` skill level limit.
- Discovery tracking is stored as world state, so it persists across sessions.
- Frost Cave cave paintings and regular runestones use separate detection and reward settings.
- Boss runestones are excluded using runestone metadata and boss-related naming checks.
- Hover-based cave painting detection is cached to reduce repeated per-frame work.


## Configuration

The mod includes server-synced configuration locking through BepInEx/ServerSync.

- `Lock Configuration`: when enabled, configuration changes are restricted to server admins.
- `Enable Cave Painting Rewards`: enables or disables Frost Cave cave painting rewards.
- `Skill Reward Mode` under `2 - Cave Painting Rewards`: choose `Flat` or `PercentageOfMax` for Frost Cave cave paintings.
- `Skill Reward Amount` under `2 - Cave Painting Rewards`: the reward value applied to Frost Cave cave paintings.
- `Enable Runestone Rewards`: enables or disables regular runestone rewards.
- `Skill Reward Mode` under `3 - Runestone Rewards`: choose `Flat` or `PercentageOfMax` for regular runestones.
- `Skill Reward Amount` under `3 - Runestone Rewards`: the reward value applied to regular runestones.


### Mostly AI Generated
I thought I would test how well AI can write a mod.  It's a bit iffy, but if you burn enough tokens you can get something that works okay. 
If I wrote this mod without AI, it might be better at the performance on the patches. Right now, it's just okay. 
Thank you to Dupontus for the mod idea. 