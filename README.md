# DiceKingdoms Casino

A lobby rule for Dice Kingdoms (off by default): a new building, the **Casino**, where you can spin a wheel of fortune once in a while. Most spins
give nothing or bring bad luck; a few win resources, and a rare jackpot sends a tsunami to everybody else.

## The Casino

- Takes 4 tiles like the Market. Only with a **University** on the island (locked in the build bar until then). No limit of its own: with the
  [Building limits](https://github.com/DiceModders/DiceKingdoms-BuildingLimits) rule on, the host sets how many an island may have (1 by default).
  The wheel is one per island however many Casinos it has.
- Costs 3 wood, 2 stone, 2 gold and 1 hammer. Burnt by an attacker, it gives him 3 gold.
- Every civilization has its own Casino model: Europe, Middle East, Viking, Ancient, Maya, and the Japan and Europe Reskin civilizations of their mods.
  A civilization from another mod gets the European one.
- Computer players do not build it.

## The wheel

Select your Casino: the tech tree button next to the pickaxe turns into the wheel button and opens the wheel window (the top bar has a Casino button for it too, while the rule is on).

- **Spin** in the build phase: the stake (3 gold) goes at once, like a market purchase. After a spin you wait 3 turns.
- The wheel has **20 faces, 5 % each**. Where it stops is decided in the next disasters phase, from the host's random numbers, so it is the same on
  every computer and nobody can know it before. The wheel turns on your screen, and your disasters wait for it.

| Face | Default | What happens |
| --- | --- | --- |
| Jackpot | 5 % | a tsunami on every other player |
| Win | 15 % | 6 gold, or 1 of each resource |
| Nothing | 45 % (the rest) | nothing |
| Bad luck | 30 % | robbers from another island, or a fire on your island |
| Catastrophe | 5 % | a tsunami and a meteorite on your own island |

## Lobby rules

The Casino's on/off tick and the wheel's numbers are in **Settings > Lobby > Casino > Casino settings** (the host edits, everybody else sees the
host's values), with a picture of the wheel they make:

| Setting | Default |
| --- | --- |
| Jackpot / win / bad luck / catastrophe chance | 5 / 15 / 30 / 5 % (steps of 5 %, together at most 100 %) |
| Stake (gold) | 3 |
| Turns between spins | 3 |
| Gold win | 6 |
| Win of each resource | 1 |

Every player in the match needs this mod (ModCore compares the mods in the lobby).

## Install

Needs BepInEx 6 (IL2CPP), [ModCore](https://github.com/DiceModders/DiceKingdoms-ModCore) 1.3.1 or newer, [UiKit](https://github.com/DiceModders/UiKit) and [ModBridge](https://github.com/DiceModders/DiceKingdoms-ModBridge). Put `DiceModders.Casino.dll` into `BepInEx\plugins`, with the game closed.

## Build

```
.\scripts\build.ps1 -GameDir "<your Dice-Kingdoms folder>"   # first time
.\scripts\build.ps1                                           # afterwards
```

The script builds the mod and copies it into the game's `BepInEx\plugins` folder (close the game first).

## How it works

- The Casino is added at the end of the game's building type list on every computer (whether the rule is on or not), so every computer numbers the
  buildings the same way. Its texts are the game's `building.24.*` keys in `texts.json`.
- A spin is one of ModCore's build actions (code 1), so the game itself sends it to everybody.
- The host adds the face's disasters to the turn's disaster list, which the game sends to everybody; every computer pays the wins from the same list.
- Its look is made from the Market each player sees (so a civilization's or a reskin's look follows), with the Casino model in place of the Market's.
  The models are `assets/models/casino-<style>.dkmesh.gz` (ModCore's `.dkmesh` text format, gzipped), built into the DLL.

## Texts

`texts.json` holds the texts in English, German, French, Russian, Ukrainian and Chinese. A `DiceModders.Casino.texts.json` next to the DLL wins line by line.

## License

MIT
