# TSMods — mods for Trials Survivors

A set of mods for **Trials Survivors**, and TSMods, a Windows app that installs and manages them.

<img width="1182" height="790" alt="image" src="https://github.com/user-attachments/assets/d4ccd17c-e5d9-4d2a-a732-c3c27c73b11b" />
<img width="1600" height="1000" alt="image" src="https://github.com/user-attachments/assets/8bc53aab-fdde-464d-bd85-0ef9d9a9258b" />
<img width="1600" height="1000" alt="image" src="https://github.com/user-attachments/assets/31d5f3ea-f38f-4b7f-92c9-2a9225dd7090" />

## Getting started

1. Download `TSMods-<version>-win-x64.zip` from the
   [latest release](https://github.com/aevv/trials-survivor-mods/releases/latest) and unzip it anywhere.
2. Run `TSMods.Loader.exe`. It finds the game through Steam.
3. Click **Install BepInEx** if it isn't installed yet. BepInEx is the mod framework the mods run on.
4. Launch the game once and close it again. The first launch with BepInEx takes a while, because it's preparing
   the game for mods.
5. Open **Get mods** and install the ones you want, then play.

The game has to be closed while you install, remove or change mods.

## What the app does

- **Get mods** installs any mod from this repo's releases.
- **Turn mods on and off** without losing them. Disabled mods are kept, along with earlier versions, so you can
  roll back if an update misbehaves.
- **Profiles** save a set of mods and switch between them in one click.
- **Settings** for each mod, with toggles, sliders and colour pickers. A mod's settings appear after it has run in
  the game once.
- **Compatibility checks.** After a game update, the app tells you which mods still work and which are broken,
  before you launch.
- **Mods on/off** launches the game vanilla without uninstalling anything.

`tsmods.exe` in the same zip is a command line version that does everything the app does. Run `tsmods help` to see
the commands.

## The mods

| mod                    | what it does |
| ---------------------- | ------------ |
| **Uncap AoE**          | Removes the cap on how many enemies one area attack can hit, so a big explosion hits the whole screen. Skills that fire a projectile per target stay capped so they don't flood the screen |
| **Elite Health Bars**  | Health bars over every elite, plus arrows at the screen edge pointing to elites you can't see |
| **Run HUD**            | Live kills, elite kills and rooms cleared next to the XP bar, plus a summary of the monster buffs in play |
| **Run History**        | Saves every run. Press F6 in the hub to browse past runs with their difficulty, map, stats and build |
| **XP Rates**           | Multiplies the XP you get from orbs during a run (2x by default) |
| **Weapon Slots**       | Raises the weapon slot limit from 5 to 6, or up to 8, and widens the DPS meter to match |
| **Room Skip**          | A SKIP button under the room objective that ends the room once its tier 3 goal is met, with full rewards |
| **Oops! All Legendary!** | Every level-up card comes up legendary. Cards with no legendary tier get their best one instead |
| **Impossible**         | A new difficulty past Unfair+: tougher monsters, more elites, and rare red elites that are tougher still |

Every mod's settings can be changed in the app's Settings tab.

## Uninstalling

Turn **Mods on/off** off to play vanilla. To remove everything, delete what BepInEx added to the game folder: the
`BepInEx` and `dotnet` folders, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.
Steam's "Verify integrity of game files" won't remove these, since they're extra files rather than changed ones.

## Building from source

The mods are BepInEx 6 IL2CPP plugins and build against files generated from your own copy of the game, so building
them needs the game installed with BepInEx. `docs/findings.md` has notes on how the game works under the hood.
