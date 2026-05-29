# FEMBOI-TOMBOI Objective Generator Mod

This is a Client-side BepInEx plugin that dynamically generates and displays tactical objectives simulating a field commander relaying orders to you (the air force).

## How it works

Since the game's `mission.json` file is loaded once at the start of a mission, modifying it at runtime has no effect until the mission is restarted. To give you a truly dynamic and reactive experience, this plugin hooks directly into the running game using BepInEx.

### Features
- **Dynamic Threat Analysis**: The plugin periodically scans the battlefield for active `Transforms` (such as carriers, destroyers, SAMs, and enemy aircraft).
- **Reactive Orders**: Depending on the hostile entities found, the commander will issue specific orders (e.g. `Maritime Strike` if ships are spotted, `SEAD/DEAD` if SAMs are found, or `Interception` for enemy aircraft).
- **Fallback Objectives**: If no specific threats are detected nearby, it will assign generic taskings such as `CAS`, `Strike`, `Ground Support`, or `Resupply`.
- **HUD Integration**: The orders are displayed directly on the screen using an immersive, tactical green text overlay on a semi-transparent black background.
