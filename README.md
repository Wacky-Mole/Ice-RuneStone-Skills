# Ice&RuneStone Skills

Ice&RuneStone Skills rewards exploration.

Find Frost Cave cave paintings and regular runestones across the world to earn one-time skill boosts for the server.

![IceLevelUP](https://wackymole.com/hosts/IceMural_levelup.png)
## Features

- Rewards exploring Frost Caves and the wider world
- One-time skill boosts from eligible cave paintings and regular runestones
- Separate settings for cave paintings and runestones
- Flat or percentage-based rewards
- Vegvisirs and boss runestones are excluded
- Multiplayer-safe discovery tracking
- Discoveries can be shared per world or earned once per player

## How it works

- Explore and discover eligible cave paintings or runestones.
- Depending on config, the first discovery in that world or for that player grants a random skill boost.
- After that, the same object is marked as claimed for the world or player.

![RuneUP](https://wackymole.com/hosts/runestone_levelup.png)

## Notes

- Rewards use Valheim's normal skill list, excluding `None` and `All`.
- Skill gains are capped at `100`.
- Discoveries persist with the world.

![AlreadyTaken](https://wackymole.com/hosts/IceMural_alreadygiven.png)
## Configuration

- `Lock Configuration`: when enabled, configuration changes are restricted to server admins.
- `Discovery Scope`: choose whether discoveries are shared for the world or tracked once per player.
- `Enable Cave Painting Rewards`: enables or disables Frost Cave cave painting rewards.
- `2 - Cave Painting Rewards`: configure cave painting reward mode and amount.
- `Enable Runestone Rewards`: enables or disables regular runestone rewards.
- `3 - Runestone Rewards`: configure runestone reward mode and amount.

## Admin Command

- `irs_resetdiscoveries`: resets all mural and runestone discoveries so players can rediscover them again.
- Server admin only.


### Mostly AI Generated
I thought I would test how well AI can write a mod.  I used Azumatt Template and GPT 5.4 (Med). It's a bit iffy, but if you burn enough tokens you can get something that works okay. I optimized it and profiled it. 
If I wrote this mod without AI, it might be better at the performance on the patches. Right now, it's just okay. This is not a hard mod to make, however the ice cave murals are challenging to target unlike RuneStones.

The AI did have a bit of problem with ServerSync and LocalizationManager which I fixed.
I am not going to start writing a lot of AI mods, but this was a fun experiment to see how well it can do and it took at lot less time.  


#### Contacts

 You can reach me in [Wolf Den](https://discord.gg/uPjjH8y52j) or [Odin Plus Team](https://discord.gg/odinplus) 
#### Thanks 
Thank you to Dupontus for the mod idea. 