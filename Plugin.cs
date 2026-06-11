using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FemboiTomboi
{
    [BepInPlugin("com.femboi.tomboi", "FEMBOI TOMBOI Objective Generator", "1.0.0")] //permanent
    public partial class FemboiTomboiPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private GUIStyle messageStyle;
        private GUIStyle bgStyle;
        private GUIStyle nukeStyle;
        private GUIStyle criticalNukeStyle;
        private GUIStyle criticalNukeCenterStyle;
        private GUIStyle incomingStyle;
        // Cached derived styles — built once, reused every frame
        private GUIStyle _mapStyle;
        private GUIStyle _sarStyle;

        public static string prefixAir = "MOMMY Command";
        public static string prefixArmy = "TOMBOI General";
        public static string prefixNavy = "FEMBOI Admiral";

        // --- Display Cache (updated by coroutine, read by OnGUI with zero alloc) ---
        private struct ObjectiveEntry { public string Text; public int Priority; public Vector3 Position; }
        private struct ThreatEntry   { public string Text; public int Priority; public float MinTof; public float BlinkRate; }

        private readonly List<ObjectiveEntry> _cachedObjectives = new List<ObjectiveEntry>(16);
        private readonly List<ThreatEntry>    _cachedCarriers   = new List<ThreatEntry>(8);
        private readonly List<ThreatEntry>    _cachedNukes      = new List<ThreatEntry>(8);
        private readonly List<ThreatEntry>    _cachedMissiles   = new List<ThreatEntry>(16);
        private int    _cachedLaunchedNukeCount   = 0;
        private int    _cachedIncomingMissileCount = 0;
        private float  _cachedGlobalMinTof         = 99999f;
        private bool   _displayCacheReady          = false;
        private ObjectiveEntry? _cachedClosestHighPriority = null;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("FEMBOI-TOMBOI Commander plugin loaded!");

            Instance = this;

            var harmony = new Harmony("com.femboi.tomboi");
            harmony.PatchAll();

            StartCoroutine(CommanderLoop());
            StartCoroutine(NukeScannerLoop());
            StartCoroutine(DatalinkSpotterLoop());
            StartCoroutine(SARScannerLoop());
            StartCoroutine(DisplayCacheLoop());
            StartCoroutine(AttachObjectiveHUDLoop());
        }

        private Unit cachedNearestPilot = null;

        private System.Collections.IEnumerator SARScannerLoop()
        {
            yield return new WaitForSeconds(5f);

            while (true)
            {
                yield return new WaitForSeconds(15f);
                
                GameManager.GetLocalAircraft(out Aircraft localAc);
                if (localAc == null || localAc.NetworkHQ == null) 
                {
                    cachedNearestPilot = null;
                    continue;
                }

                Unit nearest = null;
                float minPilotDist = float.MaxValue;
                
                foreach (var u in UnitTracker.ActivePilots)
                {
                    if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                    if (u.NetworkHQ != localAc.NetworkHQ) continue;
                    
                    float d = Vector3.Distance(localAc.transform.position, u.transform.position);
                    if (d < minPilotDist)
                    {
                        minPilotDist = d;
                        nearest = u;
                    }
                }
                
                cachedNearestPilot = nearest;
            }
        }

        // Background coroutine: recomputes all display strings at most 10x/sec
        private System.Collections.IEnumerator DisplayCacheLoop()
        {
            yield return new WaitForSeconds(5f);
            while (true)
            {
                yield return new WaitForSeconds(0.1f);
                try { RebuildDisplayCache(); } catch { }
            }
        }

        private void RebuildDisplayCache()
        {
            _cachedObjectives.Clear();
            var _seenObjectives = new System.Collections.Generic.HashSet<string>();

            // CAP targets
            foreach (var u in previouslySpottedAircraft)
            {
                if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                string sector = GetSector(u.transform.position);
                string name   = GetCleanUnitName(u);
                string txt    = $"{prefixAir}:\nActive Tasking: CAP\nTarget: {name} at Sector {sector}.\nExecute when ready.";
                if (_seenObjectives.Add(txt))
                    _cachedObjectives.Add(new ObjectiveEntry { Text = txt, Priority = 0, Position = u.transform.position });
                if (_cachedObjectives.Count >= 7) break;
            }

            // SEAD targets
            foreach (var u in previouslySpottedAirDefenses)
            {
                if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                if (_cachedObjectives.Count >= 7) break;
                string sector = GetSector(u.transform.position);
                string name   = GetCleanUnitName(u);
                string txt    = $"{prefixAir}:\nActive Tasking: SEAD/DEAD\nTarget: {name} at Sector {sector}.\nExecute when ready.";
                if (_seenObjectives.Add(txt))
                    _cachedObjectives.Add(new ObjectiveEntry { Text = txt, Priority = 0, Position = u.transform.position });
            }

            // CAS targets
            foreach (var u in previouslySpottedCAS)
            {
                if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                if (_cachedObjectives.Count >= 7) break;
                string sector = GetSector(u.transform.position);
                string name   = GetCleanUnitName(u);
                string txt    = $"{prefixArmy}:\nActive Tasking: CAS\nTarget: {name} at Sector {sector}.\nExecute when ready.";
                if (_seenObjectives.Add(txt))
                    _cachedObjectives.Add(new ObjectiveEntry { Text = txt, Priority = 1, Position = u.transform.position });
            }

            // Intercept targets
            foreach (var u in previouslySpottedIntercept)
            {
                if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                if (_cachedObjectives.Count >= 7) break;
                string sector = GetSector(u.transform.position);
                string name   = GetCleanUnitName(u);
                string txt    = $"{prefixArmy}:\nActive Tasking: INTERCEPT\nTarget: {name} at Sector {sector}.\nExecute when ready.";
                if (_seenObjectives.Add(txt))
                    _cachedObjectives.Add(new ObjectiveEntry { Text = txt, Priority = 1, Position = u.transform.position });
            }

            // Airbase threats from activeThreats
            foreach (var t in activeThreats)
            {
                if (t == null || t.ThreatUnit == null || !t.ThreatUnit.gameObject.activeInHierarchy) continue;
                if (!t.IsTargetingAirbase) continue;
                if (_cachedObjectives.Count >= 7) break;
                string sector = GetSector(t.ThreatUnit.transform.position);
                string prefix = t.TargetPrefix ?? prefixArmy;
                bool isNuke   = t.IsNuke;
                string txt    = $"{prefix}:\nActive Tasking: INTERCEPT\nTarget: {(isNuke ? "Nuclear Strike" : "Incoming")} for {t.TargetName} on Sector {sector}.\nPriority: {(isNuke ? "CRITICAL" : "HIGH")}";
                if (_seenObjectives.Add(txt))
                    _cachedObjectives.Add(new ObjectiveEntry { Text = txt, Priority = isNuke ? 2 : 1, Position = t.ThreatUnit.transform.position });
            }

            // Sort objectives by priority
            _cachedObjectives.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            if (_cachedObjectives.Count > 7) _cachedObjectives.RemoveRange(7, _cachedObjectives.Count - 7);

            // Find the closest high-priority (Priority >= 1) objective to the player
            _cachedClosestHighPriority = null;
            GameManager.GetLocalAircraft(out Aircraft localAcForObjectives);
            if (localAcForObjectives != null)
            {
                float bestDist = float.MaxValue;
                foreach (var e in _cachedObjectives)
                {
                    if (e.Priority < 1) continue;
                    float d = Vector3.Distance(localAcForObjectives.transform.position, e.Position);
                    if (d < bestDist) { bestDist = d; _cachedClosestHighPriority = e; }
                }
            }

            // Threats display
            _cachedCarriers.Clear();
            _cachedNukes.Clear();
            _cachedMissiles.Clear();
            _cachedLaunchedNukeCount   = 0;
            _cachedIncomingMissileCount = 0;
            _cachedGlobalMinTof         = 99999f;

            var nukeCounts = new System.Collections.Generic.Dictionary<string, int>();
            var nukeTotalTof = new System.Collections.Generic.Dictionary<string, float>();
            var nukeMinTof = new System.Collections.Generic.Dictionary<string, float>();
            var nukeOclock = new System.Collections.Generic.Dictionary<string, int>();
            var nukeTargetTxt = new System.Collections.Generic.Dictionary<string, string>();

            foreach (var t in activeThreats)
            {
                if (t == null || t.ThreatUnit == null) continue;
                string sector = GetSector(t.ThreatUnit.transform.position);
                if (t.IsAircraft)
                {
                    string facPrefix = GetFactionName(t.ThreatUnit.gameObject.name);
                    facPrefix = facPrefix != "Unknown Faction" ? facPrefix + " " : "";
                    string name = GetCleanUnitName(t.ThreatUnit);
                    _cachedCarriers.Add(new ThreatEntry { Text = $"[{sector}] {facPrefix}{name}", Priority = 1 });
                }
                else if (t.IsLaunched && t.IsNuke)
                {
                    _cachedLaunchedNukeCount++;
                    float tof = GetToF(t.ThreatUnit);
                    
                    int oclock = 12;
                    GameManager.GetLocalAircraft(out Aircraft localAc);
                    if (localAc != null)
                    {
                        Vector3 dir = t.ThreatUnit.transform.position - localAc.transform.position;
                        float bearing = Vector3.SignedAngle(localAc.transform.forward, dir, Vector3.up);
                        if (bearing < 0) bearing += 360f;
                        oclock = Mathf.RoundToInt(bearing / 30f);
                        if (oclock == 0) oclock = 12;
                    }

                    string seekerStr = !string.IsNullOrEmpty(t.SeekerType) ? $"[{t.SeekerType}]" : "[UNK]";
                    string targetTxt = t.IsTargetingPlayer ? seekerStr + " " : "";

                    if (!nukeCounts.ContainsKey(sector)) {
                        nukeCounts[sector] = 0;
                        nukeTotalTof[sector] = 0f;
                        nukeMinTof[sector] = 99999f;
                        nukeOclock[sector] = oclock;
                        nukeTargetTxt[sector] = targetTxt;
                    }
                    nukeCounts[sector]++;
                    nukeTotalTof[sector] += tof;
                    if (tof < nukeMinTof[sector]) {
                        nukeMinTof[sector] = tof;
                        nukeOclock[sector] = oclock;
                        nukeTargetTxt[sector] = targetTxt;
                    }
                }
                else if (t.IsLaunched && t.IsTargetingPlayer && !t.IsNuke)
                {
                    _cachedIncomingMissileCount++;
                    float tof = GetToF(t.ThreatUnit);
                    if (tof < _cachedGlobalMinTof) _cachedGlobalMinTof = tof;
                    float rate = Mathf.Clamp(10f / Mathf.Max(0.5f, tof), 0.5f, 15f);
                    
                    int oclock = 12;
                    GameManager.GetLocalAircraft(out Aircraft localAc);
                    if (localAc != null)
                    {
                        Vector3 dir = t.ThreatUnit.transform.position - localAc.transform.position;
                        float bearing = Vector3.SignedAngle(localAc.transform.forward, dir, Vector3.up);
                        if (bearing < 0) bearing += 360f;
                        oclock = Mathf.RoundToInt(bearing / 30f);
                        if (oclock == 0) oclock = 12;
                    }

                    string seekerStr = !string.IsNullOrEmpty(t.SeekerType) ? $"[{t.SeekerType}]" : "[UNK]";
                    _cachedMissiles.Add(new ThreatEntry { Text = $"[{seekerStr}] {t.ThreatUnit.name.Replace("(Clone)", "")} | Dir:{oclock} o'clock | ToF:{tof:F0}s", MinTof = tof, BlinkRate = rate });
                }
            }

            foreach (var kvp in nukeCounts) {
                string sec = kvp.Key;
                int count = kvp.Value;
                float minTof = nukeMinTof[sec];
                float avgTof = nukeTotalTof[sec] / count;
                int oclock = nukeOclock[sec];
                string tTxt = nukeTargetTxt[sec];
                
                string txt = count > 1 
                    ? $"{tTxt}[{sec}] {count} Nukes | Dir:{oclock} o'clock | Avg ToF:{avgTof:F0}s"
                    : $"{tTxt}[{sec}] Nuke | Dir:{oclock} o'clock | ToF:{minTof:F0}s";
                    
                _cachedNukes.Add(new ThreatEntry { Text = txt, MinTof = minTof, BlinkRate = 10f });
            }

            _displayCacheReady = true;
        }

        private void OnGUI()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameWorld") return;
            if (Time.timeScale < 0.01f) return; // Hide when paused

            if (Event.current.type != EventType.Repaint) return;

            if (messageStyle == null)
            {
                messageStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true
                };
                messageStyle.normal.textColor = new Color(0.1f, 0.9f, 0.2f);

                nukeStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight, fontSize = 11 };
                nukeStyle.normal.textColor = Color.yellow;

                criticalNukeStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight, fontSize = 11 };

                criticalNukeCenterStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperCenter, fontSize = 12 };

                incomingStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight, fontSize = 11 };
                incomingStyle.normal.textColor = new Color(1f, 0.4f, 0f); // Orange

                Texture2D bgTex = new Texture2D(1, 1);
                bgTex.SetPixel(0, 0, new Color(0, 0, 0, 0.6f));
                bgTex.Apply();

                bgStyle = new GUIStyle();
                bgStyle.normal.background = bgTex;

                // Build derived styles ONCE here
                _mapStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
                _sarStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperLeft, fontSize = 13 };
                _sarStyle.normal.textColor = Color.white;
            }

            bool mapOpen = MapTracker.IsMapOpen;
            float screenW = Screen.width;
            float screenH = Screen.height;

            // 1. Draw Map Priority Objectives (Bottom Left, 1 objective only)
            if (mapOpen && _displayCacheReady)
            {
                float mapWidth  = 380f;
                float mapHeight = 90f;
                float mapX = 20f;

                int displayCount = Mathf.Min(_cachedObjectives.Count, 1);
                if (displayCount > 0)
                {
                    bool isObjRed = (Time.time * 10f) % 1f < 0.5f;
                    float y = screenH - 20f - mapHeight;
                    int prio = _cachedObjectives[0].Priority;
                    if (prio == 0)      _mapStyle.normal.textColor = messageStyle.normal.textColor;
                    else if (prio == 1) _mapStyle.normal.textColor = Color.yellow;
                    else                _mapStyle.normal.textColor = isObjRed ? Color.red : Color.yellow;

                    GUI.Box(new Rect(mapX - 10, y - 10, mapWidth + 20, mapHeight + 20), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(mapX, y, mapWidth, mapHeight), _cachedObjectives[0].Text, _mapStyle);
                    _mapStyle.normal.textColor = messageStyle.normal.textColor;

                    // Header
                    float headerY = y - 35f;
                    GUI.Box(new Rect(mapX - 10, headerY - 5, mapWidth + 20, 30), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(mapX, headerY, mapWidth, 25), "ACTIVE PRIORITY OBJECTIVES", _mapStyle);
                }

                // SAR Box (Bottom Right)
                GameManager.GetLocalAircraft(out Aircraft localAc);
                if (localAc != null && localAc.NetworkHQ != null && cachedNearestPilot != null && cachedNearestPilot.gameObject.activeInHierarchy && !cachedNearestPilot.disabled)
                {
                    Unit nearestPilot = cachedNearestPilot;
                    float minPilotDist = Vector3.Distance(localAc.transform.position, nearestPilot.transform.position);
                    Vector3 dirToPilot = nearestPilot.transform.position - localAc.transform.position;
                    float bearing = Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(dirToPilot, Vector3.up), Vector3.up);
                    if (bearing < 0) bearing += 360f;
                    float distanceNm = minPilotDist / 1852f;

                    float sarWidth = 380f;
                    float sarHeight = 60f;
                    float sarX = screenW - sarWidth - 20f;
                    float sarY = screenH - sarHeight - 20f;
                    string sarMsg = $"{prefixAir}:\nNearest Disembarked Pilot: {bearing:F0}° at {distanceNm:F1} NM";
                    GUI.Box(new Rect(sarX - 10, sarY - 10, sarWidth + 20, sarHeight + 20), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(sarX, sarY, sarWidth, sarHeight), sarMsg, _sarStyle);
                }
            }

            // 2. New Objective Feed: rendering moved to the in-cockpit ObjectiveHUDIndicator (HMD)
            // when the map is closed; just keep the log pruned of expired entries here.
            if (messageLog.Count > 0)
            {
                messageLog.RemoveAll(m => Time.time > m.ExpirationTime);
            }

            // 3. Nuke/Missile Threats Overlay (Top Right) — zero allocation, reads cached data
            if (_displayCacheReady && (_cachedCarriers.Count > 0 || _cachedNukes.Count > 0 || _cachedMissiles.Count > 0))
            {
                const float alertW = 420f;
                float totalBlockHeight = (_cachedCarriers.Count > 0 ? 30f : 0f) 
                                       + (_cachedNukes.Count > 0 ? 30f + _cachedNukes.Count * 25f : 0f)
                                       + (_cachedMissiles.Count > 0 ? 30f + _cachedMissiles.Count * 25f : 0f);
                float currentY = mapOpen ? 20f : (screenH / 2f - totalBlockHeight / 2f);
                bool isNukeRed = (Time.time * 1f) % 1f < 0.5f;
                criticalNukeStyle.normal.textColor = isNukeRed ? Color.red : Color.yellow;
                float rightEdge = screenW - 10f;

                // Carriers
                if (_cachedCarriers.Count > 0)
                {
                    GUI.Box(new Rect(rightEdge - alertW - 10, currentY - 5, alertW + 20, 30), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(rightEdge - alertW, currentY, alertW, 25), $"WARNING: {_cachedCarriers.Count} NUCLEAR CARRIER(S)", nukeStyle);
                    currentY += 30f;
                }

                // Nukes
                if (_cachedNukes.Count > 0)
                {
                    float minNukeTof = 99999f;
                    for (int i = 0; i < _cachedNukes.Count; i++)
                        if (_cachedNukes[i].MinTof < minNukeTof) minNukeTof = _cachedNukes[i].MinTof;

                    string nukeHeader = $"CRITICAL: {_cachedLaunchedNukeCount} NUKE(S) INBOUND | ToF:{minNukeTof:F0}s";
                    
                    float blockHeight = 30f + (_cachedNukes.Count * 25f);
                    float detailY = currentY;
                    
                    bool isNukeRedHeader = (Time.time * 1f) % 1f < 0.5f;
                    criticalNukeStyle.normal.textColor = isNukeRedHeader ? Color.red : Color.yellow;
                    
                    // Header
                    GUI.Box(new Rect(rightEdge - alertW - 10, detailY - 5, alertW + 20, 30), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(rightEdge - alertW, detailY, alertW, 25), nukeHeader, criticalNukeStyle);
                    detailY += 30f;

                    // Details
                    for (int i = 0; i < _cachedNukes.Count; i++)
                    {
                        GUI.Box(new Rect(rightEdge - alertW - 10, detailY - 5, alertW + 20, 25), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(rightEdge - alertW, detailY, alertW, 25), _cachedNukes[i].Text, criticalNukeStyle);
                        detailY += 25f;
                    }
                    currentY = detailY;
                }

                // Missiles
                if (_cachedMissiles.Count > 0)
                {
                    float headerBlinkRate = Mathf.Clamp(10f / Mathf.Max(0.5f, _cachedGlobalMinTof), 0.5f, 15f);
                    bool isHeaderRed = (Time.time * headerBlinkRate) % 1f < 0.5f;
                    incomingStyle.normal.textColor = isHeaderRed ? Color.red : Color.yellow;
                    GUI.Box(new Rect(rightEdge - alertW - 10, currentY - 5, alertW + 20, 30), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(rightEdge - alertW, currentY, alertW, 25), $"WARNING: {_cachedIncomingMissileCount} INCOMING MISSILE(S)", incomingStyle);
                    currentY += 30f;

                    for (int i = 0; i < _cachedMissiles.Count; i++)
                    {
                        bool isRed = (Time.time * _cachedMissiles[i].BlinkRate) % 1f < 0.5f;
                        incomingStyle.normal.textColor = isRed ? Color.red : Color.yellow;
                        GUI.Box(new Rect(rightEdge - alertW - 10, currentY - 5, alertW + 20, 25), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(rightEdge - alertW, currentY, alertW, 25), _cachedMissiles[i].Text, incomingStyle);
                        currentY += 25f;
                    }
                    // Restore orange
                    incomingStyle.normal.textColor = new Color(1f, 0.4f, 0f);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Unit))]
    public static class UnitTracker
    {
        public static HashSet<Unit> ActiveUnits = new HashSet<Unit>();
        public static HashSet<Unit> ActivePilots = new HashSet<Unit>();
        private static Type EjectedPilotType = AccessTools.TypeByName("EjectedPilot");

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        static void AwakePostfix(Unit __instance)
        {
            if (__instance != null)
            {
                ActiveUnits.Add(__instance);
                
                string n = __instance.name;
                if (n != null && (n.IndexOf("pilot", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("eject", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    ActivePilots.Add(__instance);
                }
                else if (EjectedPilotType != null && __instance.GetComponentInChildren(EjectedPilotType, true) != null)
                {
                    ActivePilots.Add(__instance);
                }
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        static void OnDestroyPostfix(Unit __instance)
        {
            if (__instance != null)
            {
                ActiveUnits.Remove(__instance);
                ActivePilots.Remove(__instance);
            }
        }
    }

    [HarmonyPatch]
    public static class MapTracker
    {
        public static bool IsMapOpen = false;

        [HarmonyPatch(typeof(DynamicMap), "Maximize")]
        [HarmonyPostfix]
        static void MaximizePostfix()
        {
            IsMapOpen = true;
        }

        [HarmonyPatch(typeof(DynamicMap), "Minimize")]
        [HarmonyPostfix]
        static void MinimizePostfix()
        {
            IsMapOpen = false;
        }
    }
}
