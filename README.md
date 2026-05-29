# FEMBOI-TOMBOI
**Fleet Escort & Maritime-support Battlefield Operations Intelligence / Tactical-Offensive & Massive Ballistics Operations Intelligence**
![1000103531](https://github.com/user-attachments/assets/bcee1000-60f4-4fa9-8703-fcdeea7b3bf6)

FEMBOI-TOMBOI is a highly optimized, dynamic Field Commander HUD mod that brings a live military command center directly into your cockpit. By continually analyzing the active battlefield, it generates realistic, reactive tactical orders and provides critical early warning capabilities against mass-destruction weapons.

## Features

### 📡 Dynamic Tactical Objective Generator
Receive continuous, context-aware mission orders from your Field Commander. The mod intelligently scans the battlefield for enemy assets and friendly logistical needs, generating objectives such as:
- **SEAD/DEAD** (Suppression/Destruction of Enemy Air Defenses)
- **Air Interdiction & Interception**
- **CAS** (Close Air Support)
- **Maritime Strike**
- **Ground Support & Strike**
- **Resupply** (Detects surface units critically low on missile armament)

**HUD Integration:** New objectives are relayed via a clean, tactical feed. When you open the full-screen tactical map, your Active Priority Objective list is displayed for quick review.

### ☢️ Advanced Nuclear Threat Scanner (TOMBOI)
Never get caught off-guard by strategic weapons again. The TOMBOI subsystem provides a dedicated, high-priority bottom-right HUD for tracking nuclear threats.
- **Payload Detection:** Automatically detects enemy aircraft (or ground units) carrying nuclear payloads (Genie, MIRV, 250kt, etc.) and logs their sector.
- **Live Missile Tracking:** When a nuclear weapon is launched, the HUD immediately triggers a flashing Red/Yellow critical strobe.
- **Telemetry Data:** Provides real-time tracking of the inbound nuke's Sector, Altitude, Speed (Mach), and a live Time-of-Flight (ToF) countdown until impact.

### ⚡ Zero-Impact Performance
Engineered using strict Modding Optimization Masterclass standards:
- **No Stuttering:** Completely eliminates heavy `FindObjectsOfType` polling and scene-load scanning.
- **Zero Allocations:** Utilizes highly efficient `StringComparison.OrdinalIgnoreCase` checks and cached Harmony patches for unit tracking.
- **Seamless Gameplay:** Your FPS remains completely unaffected, no matter how many units or missiles are active in the warzone.

## Installation
Requires BepInEx. Drop the `FEMBOI-TOMBOI.dll` into your `BepInEx/plugins` folder and launch the game. 

Good luck out there, pilot!
