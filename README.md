# Escape From Antonchik

[![Unity](https://img.shields.io/badge/Unity-2023.1.22f1-black?logo=unity&logoColor=white)](https://unity.com/)
[![Made with C#](https://img.shields.io/badge/Made%20with-C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-HDRP%2015.0.7-1a73e8)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/index.html)
[![Platforms](https://img.shields.io/badge/Platforms-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](https://github.com/alxlemesh/Escape-From-Antonchik/actions/workflows/build.yml)

Escape From Antonchik is a Unity first-person project built with Unity 2023.1.22f1.

## Project Structure

- `Assets/Scenes` - Unity scenes.
- `Assets/Scripts` - gameplay and interaction scripts.
- `Assets/Animations` - animation clips, controllers, and setup data.
- `Assets/Prefabs` - reusable Unity prefabs.
- `Assets/SFX` - audio assets.
- `Assets/Fonts` - text and emoji font assets.
- `Assets/Editor` - editor utilities for setup and animation workflows.
- `ProjectSettings` - Unity project configuration.

## Getting Started

1. Install Unity `2023.1.22f1`.
2. Clone this repository.
3. Open the repository root in Unity Hub.
4. Let Unity import the project assets.
5. Open a scene from `Assets/Scenes` and press Play.

## Cheat Codes

In play mode, press **6** then **7** (within ~1.25s) to open the cheat box, type a
code, and press **Enter** (`Esc` cancels). Codes are case-insensitive.

| Code               | Aliases             | Effect                                                                                   |
| ------------------ | ------------------- | ---------------------------------------------------------------------------------------- |
| `noclip`           | `fly`               | Toggle noclip free-fly (collisions off; `Space`/`Ctrl` up/down, `Shift` to move faster). |
| `clip`             | `walk`              | Turn noclip off.                                                                         |
| `scene2`           | `gamescene`         | Load the `gamePlay` scene (inventory carries over).                                      |
| `scene3`           | `continuegamescene` | Load the `continueGamePlay` (yard) scene (inventory carries over).                       |
| `task1`            | `snus`              | Jump the Antonchik quest to task 1 (find the snus).                                      |
| `task2`            | `keys`              | Jump the quest to task 2 (find the gate keys).                                           |
| `task3`            | `car`               | Jump to task 3 and hand over the car keys.                                               |
| `gun`              | `ammo`              | Equip a pistol and refill ammo.                                                          |
| `stats`            | `fps`               | Toggle the FPS / debug stats overlay.                                                    |
| `debugItemSpawn`   |                     | Spawn 100 gate-key props near the fences (ground-only spawn test).                       |
| `debugSpawnPoints` |                     | Lay key props on a grid across the whole map to visualize ground spawn coverage.         |

The two `debug*` codes are diagnostic stress-spawns — they drop many physics props
into the scene, so expect a framerate cost until the scene reloads.

## Notes

- Generated Unity folders such as `Library`, `Temp`, `Obj`, `Build`, `Builds`, `Logs`, and `UserSettings` are ignored.
- macOS `.DS_Store` files are ignored and should not be committed.
- `Features.md` contains implementation notes for pickup animation formulas.
