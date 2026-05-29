# FEMBOI-TOMBOI
**Fleet Escort & Maritime-support Battlefield Operations Intelligence / Tactical-Offensive & Massive Ballistics Operations Intelligence**

![1000103531](https://github.com/user-attachments/assets/bcee1000-60f4-4fa9-8703-fcdeea7b3bf6)

FEMBOI-TOMBOI is a highly optimized, dynamic Field Commander HUD mod that brings a live military command center directly into your cockpit. By continually analyzing the active battlefield, it generates realistic, reactive tactical orders and provides critical early warning capabilities against mass-destruction weapons.

## Features

### 📡 Dynamic Tactical Objective Generator & Command Hierarchy
Receive continuous, context-aware mission orders from your Field Commanders. The mod intelligently scans the battlefield for enemy assets, inbound strikes, and friendly logistical needs, grouping identical threats to avoid notification spam. Orders are dynamically issued by the relevant authority:
- **FEMBOI Admiral:** Coordinates defense and taskings for Naval assets (Carriers, Cruisers, Destroyers).
- **TOMBOI General:** Oversees ground operations and static base defense.
- **MOMMY Command:** Directs aerial intercepts and aircraft defense taskings.

It generates objectives such as:
- **Fleet & Base Defense (INTERCEPT):** Detects missiles inbound on friendly Airbases or Ships, alerting you to defend them.
- **SEAD/DEAD** (Suppression/Destruction of Enemy Air Defenses)
- **Air Interdiction & Interception (CAP)**
- **CAS** (Close Air Support)
- **Maritime Strike**
- **Ground Support & Strike**
- **Resupply** (Detects surface units critically low on missile armament)

**HUD Integration:** New objectives are relayed via a clean, top-centered tactical feed. When you open the full-screen tactical map, your Active Priority Objective list is displayed on the top right. 
- Objectives are smartly grouped by Sector and Target to prevent screen clutter (capped at 7).
- Automatically sorted by priority: **Nuclear Strikes** (Flashing Red/Yellow) > **Missile Strikes** (Yellow) > **Standard Taskings** (Green).

### ☢️ Advanced Nuclear & Air Threat Scanner
Never get caught off-guard by strategic weapons again. The scanner subsystem provides a dedicated, high-priority top-right HUD (or map-integrated overlay) for tracking nuclear and incoming threats.
- **Payload Detection:** Automatically detects enemy aircraft (or ground units) carrying nuclear payloads (Genie, MIRV, 250kt, etc.) and logs their sector.
- **Strategic Strike Detection:** When a nuclear weapon is launched against an allied base, fleet, or you, the HUD triggers a `CRITICAL` flashing Red/Yellow strobe.
- **Incoming Warning:** Provides explicit `WARNING` alerts when a standard missile is actively tracking and heading toward friendly strategic assets or your aircraft!
- **Telemetry Data:** Provides real-time tracking of the inbound nuke's or incoming missile's Sector, Altitude, Speed (Mach), and a live Time-of-Flight (ToF) countdown until impact.

### ⚡ Zero-Impact Performance
Engineered using strict Modding Optimization Masterclass standards:
- **No Stuttering:** Completely eliminates heavy `FindObjectsOfType` polling and scene-load scanning.
- **Zero Allocations:** Utilizes highly efficient `StringComparison.OrdinalIgnoreCase` checks and cached Harmony patches for unit tracking.
- **Seamless Gameplay:** Your FPS remains completely unaffected, no matter how many units or missiles are active in the warzone.

## Installation
Requires BepInEx. Drop the `FEMBOI-TOMBOI.dll` into your `BepInEx/plugins` folder and launch the game. 

Good luck out there, pilot!
