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
        private GUIStyle incomingStyle;

        public static string prefixAir = "MOMMY Command";
        public static string prefixArmy = "TOMBOI General";
        public static string prefixNavy = "FEMBOI Admiral";

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("FEMBOI-TOMBOI Commander plugin loaded!");

            var harmony = new Harmony("com.femboi.tomboi");
            harmony.PatchAll();

            StartCoroutine(CommanderLoop());
            StartCoroutine(NukeScannerLoop());
            StartCoroutine(DatalinkSpotterLoop());
            StartCoroutine(SARScannerLoop());
        }

        private Unit cachedNearestPilot = null;

        private System.Collections.IEnumerator SARScannerLoop()
        {
            yield return new WaitForSeconds(5f);

            while (true)
            {
                yield return new WaitForSeconds(1f);
                
                GameManager.GetLocalAircraft(out Aircraft localAc);
                if (localAc == null || localAc.NetworkHQ == null) 
                {
                    cachedNearestPilot = null;
                    continue;
                }

                Unit nearest = null;
                float minPilotDist = float.MaxValue;
                
                foreach (var u in UnitTracker.ActiveUnits)
                {
                    if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                    if (u.NetworkHQ != localAc.NetworkHQ) continue;
                    
                    bool isPilot = false;
                    string n = u.gameObject.name;
                    if (n != null && (n.IndexOf("pilot", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("eject", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        isPilot = true;
                    }
                    else
                    {
                        var comps = u.GetComponentsInChildren<Component>();
                        if (comps.Any(c => c != null && c.GetType().Name.Contains("EjectedPilot"))) isPilot = true;
                    }
                    
                    if (isPilot)
                    {
                        float d = Vector3.Distance(localAc.transform.position, u.transform.position);
                        if (d < minPilotDist)
                        {
                            minPilotDist = d;
                            nearest = u;
                        }
                    }
                }
                
                cachedNearestPilot = nearest;
            }
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

                nukeStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight };
                nukeStyle.normal.textColor = Color.yellow;

                criticalNukeStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight };

                incomingStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperRight };
                incomingStyle.normal.textColor = new Color(1f, 0.4f, 0f); // Orange

                Texture2D bgTex = new Texture2D(1, 1);
                bgTex.SetPixel(0, 0, new Color(0, 0, 0, 0.6f));
                bgTex.Apply();

                bgStyle = new GUIStyle();
                bgStyle.normal.background = bgTex;
            }

            bool mapOpen = MapTracker.IsMapOpen;
            float screenW = Screen.width;
            float screenH = Screen.height;

            // 1. Draw Map Full Screen Objectives (Top Left)
            if (mapOpen)
            {
                float mapWidth = 380f;
                float mapHeight = 110f;
                float mapX = 20f; // Top left
                float mapBaseY = 20f; // Top left
                var dynamicObjectives = new List<(string Text, int Priority)>(); // 0=normal, 1=high, 2=critical

                var validSpotted = previouslySpottedAircraft.Where(u => u != null && u.gameObject != null && u.gameObject.activeInHierarchy && !u.disabled).ToList();
                if (validSpotted.Count > 0)
                {
                    var groupedBySector = validSpotted.GroupBy(u => GetSector(u.transform.position)).ToList();
                    
                    foreach (var sectorGroup in groupedBySector)
                    {
                        var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                        List<string> targetStrings = new List<string>();
                        foreach (var nameGroup in groupedByName)
                        {
                            int count = nameGroup.Count();
                            if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                            else targetStrings.Add(nameGroup.Key);
                        }
                        
                        string targetDesc = string.Join(" ; ", targetStrings);
                        string locationStr = "Sector " + sectorGroup.Key;
                        string msg = $"{prefixAir}:\nActive Tasking: CAP\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                        dynamicObjectives.Add((msg, 0));
                    }
                }
                
                var validSpottedAD = previouslySpottedAirDefenses.Where(u => u != null && u.gameObject != null && u.gameObject.activeInHierarchy && !u.disabled).ToList();
                if (validSpottedAD.Count > 0)
                {
                    var groupedBySector = validSpottedAD.GroupBy(u => GetSector(u.transform.position)).ToList();
                    foreach (var sectorGroup in groupedBySector)
                    {
                        var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                        List<string> targetStrings = new List<string>();
                        foreach (var nameGroup in groupedByName)
                        {
                            int count = nameGroup.Count();
                            if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                            else targetStrings.Add(nameGroup.Key);
                        }
                        
                        string targetDesc = string.Join(" ; ", targetStrings);
                        string locationStr = "Sector " + sectorGroup.Key;
                        string msg = $"{prefixAir}:\nActive Tasking: SEAD/DEAD\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                        dynamicObjectives.Add((msg, 0));
                    }
                }

                var validSpottedCAS = previouslySpottedCAS.Where(u => u != null && u.gameObject != null && u.gameObject.activeInHierarchy && !u.disabled).ToList();
                if (validSpottedCAS.Count > 0)
                {
                    var groupedBySector = validSpottedCAS.GroupBy(u => GetSector(u.transform.position)).ToList();
                    foreach (var sectorGroup in groupedBySector)
                    {
                        var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                        List<string> targetStrings = new List<string>();
                        foreach (var nameGroup in groupedByName)
                        {
                            int count = nameGroup.Count();
                            if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                            else targetStrings.Add(nameGroup.Key);
                        }
                        
                        string targetDesc = string.Join(" ; ", targetStrings);
                        string locationStr = "Sector " + sectorGroup.Key;
                        string msg = $"{prefixArmy}:\nActive Tasking: CAS\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                        dynamicObjectives.Add((msg, 1));
                    }
                }

                var validSpottedIntercept = previouslySpottedIntercept.Where(u => u != null && u.gameObject != null && u.gameObject.activeInHierarchy && !u.disabled).ToList();
                if (validSpottedIntercept.Count > 0)
                {
                    var groupedBySector = validSpottedIntercept.GroupBy(u => GetSector(u.transform.position)).ToList();
                    foreach (var sectorGroup in groupedBySector)
                    {
                        var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                        List<string> targetStrings = new List<string>();
                        foreach (var nameGroup in groupedByName)
                        {
                            int count = nameGroup.Count();
                            if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                            else targetStrings.Add(nameGroup.Key);
                        }
                        
                        string targetDesc = string.Join(" ; ", targetStrings);
                        string locationStr = "Sector " + sectorGroup.Key;
                        string msg = $"{prefixArmy}:\nActive Tasking: INTERCEPT\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                        dynamicObjectives.Add((msg, 1)); // Priority 1 so it's yellow instead of flashing red
                    }
                }

                if (dynamicObjectives.Count > 0 || activeThreats.Any(t => t.IsTargetingAirbase && t.ThreatUnit != null && t.ThreatUnit.gameObject.activeInHierarchy))
                {
                    // Group Airbase Threats
                    var airbaseThreats = activeThreats.Where(t => t.IsTargetingAirbase && t.ThreatUnit != null && t.ThreatUnit.gameObject.activeInHierarchy).ToList();
                    var groupedAirbaseThreats = airbaseThreats.GroupBy(t => new { Sector = GetSector(t.ThreatUnit.transform.position), Target = t.TargetName }).ToList();
                    
                    foreach (var group in groupedAirbaseThreats)
                    {
                        bool hasNuke = group.Any(t => t.IsNuke);
                        string threatType = hasNuke ? "Nuclear Strike" : "Incoming";
                        string priorityText = hasNuke ? "CRITICAL" : "HIGH";
                        string prefix = group.FirstOrDefault()?.TargetPrefix ?? prefixArmy;
                        string msg = $"{prefix}:\nActive Tasking: INTERCEPT\nTarget: {threatType} for {group.Key.Target} on Sector {group.Key.Sector}.\nPriority: {priorityText}";
                        dynamicObjectives.Add((msg, hasNuke ? 2 : 1));
                    }

                    var finalObjectives = dynamicObjectives.OrderByDescending(o => o.Priority).Take(7).ToList();

                    GUIStyle mapStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperLeft };
                    GUI.Box(new Rect(mapX - 10, mapBaseY - 10, mapWidth + 20, 40), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(mapX, mapBaseY - 5, mapWidth, 30), "ACTIVE PRIORITY OBJECTIVES", mapStyle);

                    for (int i = 0; i < finalObjectives.Count; i++)
                    {
                        float y = mapBaseY + 40f + (i * (mapHeight + 10f));
                        GUIStyle itemStyle = new GUIStyle(mapStyle);
                        if (finalObjectives[i].Priority == 1)
                            itemStyle.normal.textColor = Color.yellow;
                        else if (finalObjectives[i].Priority == 2)
                        {
                            bool isObjRed = (Time.time * 10f) % 1f < 0.5f;
                            itemStyle.normal.textColor = isObjRed ? Color.red : Color.yellow;
                        }

                        GUI.Box(new Rect(mapX - 10, y - 10, mapWidth + 20, mapHeight + 20), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(mapX, y, mapWidth, mapHeight), finalObjectives[i].Text, itemStyle);
                    }
                }

                // Add SAR Box (Bottom Left)
                GameManager.GetLocalAircraft(out Aircraft localAc);
                if (localAc != null && localAc.NetworkHQ != null && cachedNearestPilot != null && cachedNearestPilot.gameObject.activeInHierarchy && !cachedNearestPilot.disabled)
                {
                    Unit nearestPilot = cachedNearestPilot;
                    float minPilotDist = Vector3.Distance(localAc.transform.position, nearestPilot.transform.position);
                    
                    if (nearestPilot != null)
                    {
                        Vector3 dirToPilot = nearestPilot.transform.position - localAc.transform.position;
                        float bearing = Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(dirToPilot, Vector3.up), Vector3.up);
                        if (bearing < 0) bearing += 360f;
                        
                        float distanceNm = minPilotDist / 1852f; // NM
                        
                        GUIStyle sarStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperLeft };
                        sarStyle.normal.textColor = Color.white;
                        
                        float sarWidth = 380f;
                        float sarHeight = 60f;
                        float sarX = 20f;
                        float sarY = screenH - sarHeight - 20f; // Bottom left
                        
                        string msg = $"{prefixAir}:\nNearest Disembarked Pilot: {bearing:F0}° at {distanceNm:F1} NM";
                        GUI.Box(new Rect(sarX - 10, sarY - 10, sarWidth + 20, sarHeight + 20), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(sarX, sarY, sarWidth, sarHeight), msg, sarStyle);
                    }
                }
            }

            // 2. Draw Top Center New Objective Feed (only in game, NOT on map)
            if (messageLog.Count > 0 && !mapOpen)
            {
                messageLog.RemoveAll(m => Time.time > m.ExpirationTime);

                float width = 600f;
                float height = 110f; 
                float x = (screenW - width) / 2f; // Top Center
                float baseY = 20f;
                GUIStyle centeredStyle = new GUIStyle(messageStyle) { alignment = TextAnchor.UpperCenter };

                for (int i = 0; i < messageLog.Count; i++)
                {
                    int reverseIndex = messageLog.Count - 1 - i; // Newest at top
                    float y = baseY + (reverseIndex * (height + 10f));

                    var baseColor = messageStyle.normal.textColor;
                    if (messageLog[i].Priority == 1) baseColor = Color.yellow;
                    else if (messageLog[i].Priority == 2) 
                    {
                        bool isMsgRed = (Time.time * 6f) % 1f < 0.5f;
                        baseColor = isMsgRed ? Color.red : Color.yellow;
                    }

                    float elapsed = Time.time - messageLog[i].Timestamp;
                    float totalDur = messageLog[i].ExpirationTime - messageLog[i].Timestamp;
                    baseColor.a = Mathf.Clamp01(1f - (elapsed / totalDur));
                    centeredStyle.normal.textColor = baseColor;

                    GUI.Box(new Rect(x - 10, y - 10, width + 20, height + 20), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(x, y, width, height), messageLog[i].Text, centeredStyle);
                }

                var restore = messageStyle.normal.textColor;
                restore.a = 1f;
                messageStyle.normal.textColor = restore;
            }

            // 3. Nuke Threats Overlay (Top Right, visible everywhere)
            if (activeThreats.Count > 0)
            {
                var validThreats = activeThreats.Where(t => t != null && t.ThreatUnit != null && t.ThreatUnit.gameObject != null).ToList();
                var carriers = validThreats.Where(t => t.IsAircraft).ToList();
                var launchedNukes = validThreats.Where(t => t.IsLaunched && t.IsNuke).ToList();
                var incomingMissiles = validThreats.Where(t => t.IsLaunched && t.IsTargetingPlayer && !t.IsNuke).ToList();

                var groupedCarriers = carriers.GroupBy(t => new { Sector = GetSector(t.ThreatUnit.transform.position), Name = GetCleanUnitName(t.ThreatUnit), Fac = GetFactionName(t.ThreatUnit.gameObject.name) }).ToList();
                var groupedNukes = launchedNukes.GroupBy(t => new { Sector = GetSector(t.ThreatUnit.transform.position), TargetingPlayer = t.IsTargetingPlayer }).ToList();
                var groupedMissiles = incomingMissiles.GroupBy(t => new { Sector = GetSector(t.ThreatUnit.transform.position) }).ToList();

                float totalHeight = 0f;
                if (groupedCarriers.Count > 0) totalHeight += 40f + (groupedCarriers.Count * 30f);
                if (groupedNukes.Count > 0) totalHeight += 40f + (groupedNukes.Count * 30f) + (groupedCarriers.Count > 0 ? 10f : 0f);
                if (groupedMissiles.Count > 0) totalHeight += 40f + (groupedMissiles.Count * 30f) + ((groupedCarriers.Count > 0 || groupedNukes.Count > 0) ? 10f : 0f);

                float currentY = mapOpen ? 20f : (screenH - totalHeight) / 2f;
                bool isNukeRed = (Time.time * 10f) % 1f < 0.5f; // Fast blink for nukes
                criticalNukeStyle.normal.textColor = isNukeRed ? Color.red : Color.yellow;

                if (groupedCarriers.Count > 0)
                {
                    GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 40), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(screenW - 360, currentY, 350, 30), "WARNING: NUCLEAR CARRIERS", nukeStyle);
                    currentY += 40f;

                    foreach (var group in groupedCarriers)
                    {
                        string facPrefix = group.Key.Fac != "Unknown Faction" ? group.Key.Fac + " " : "";
                        int count = group.Count();
                        string countStr = count > 1 ? $"{count}x " : "";
                        string msg = $"[{group.Key.Sector}] {facPrefix}{countStr}{group.Key.Name}";
                        
                        GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 30), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(screenW - 360, currentY, 350, 30), msg, nukeStyle);
                        currentY += 30f;
                    }
                    if (groupedNukes.Count > 0 || groupedMissiles.Count > 0) currentY += 10f; // Gap
                }

                if (groupedNukes.Count > 0)
                {
                    GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 40), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(screenW - 360, currentY, 350, 30), $"CRITICAL: {launchedNukes.Count} NUKES INBOUND", criticalNukeStyle);
                    currentY += 40f;

                    foreach (var group in groupedNukes)
                    {
                        int count = group.Count();
                        float avgSpeed = group.Average(t => t.ThreatUnit.rb != null ? t.ThreatUnit.rb.velocity.magnitude * 3.6f : 0f);
                        float minTof = group.Min(t => GetToF(t.ThreatUnit));
                        float maxTof = group.Max(t => GetToF(t.ThreatUnit));
                        
                        string seekerStr = !string.IsNullOrEmpty(group.FirstOrDefault()?.SeekerType) ? $"[{group.FirstOrDefault().SeekerType}]" : "[UNK]";
                        string targetText = group.Key.TargetingPlayer ? $"{seekerStr} " : ""; //replace with seeker type
                        string countStr = count > 1 ? $"{count}x Nukes" : "Nuke";
                        string tofStr = Mathf.Approximately(minTof, maxTof) ? $"{minTof:F0}s" : $"{minTof:F0}-{maxTof:F0}s";
                        
                        string msg = $"{targetText}[{group.Key.Sector}] {countStr} | Spd: {avgSpeed:F0}km/h | ToF: {tofStr}";
                        
                        GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 30), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(screenW - 360, currentY, 350, 30), msg, criticalNukeStyle);
                        currentY += 30f;
                    }
                    if (groupedMissiles.Count > 0) currentY += 10f; // Gap
                }

                if (groupedMissiles.Count > 0)
                {
                    float globalMinTof = incomingMissiles.Min(t => GetToF(t.ThreatUnit));
                    float headerBlinkRate = Mathf.Clamp(20f / Mathf.Max(1f, globalMinTof), 2f, 15f);
                    bool isHeaderRed = (Time.time * headerBlinkRate) % 1f < 0.5f;
                    GUIStyle headerStyle = new GUIStyle(incomingStyle);
                    headerStyle.normal.textColor = isHeaderRed ? Color.red : Color.yellow;

                    GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 40), GUIContent.none, bgStyle);
                    GUI.Label(new Rect(screenW - 360, currentY, 350, 30), $"WARNING: {incomingMissiles.Count} INCOMING MISSILES", headerStyle);
                    currentY += 40f;

                    foreach (var group in groupedMissiles)
                    {
                        int count = group.Count();
                        float avgSpeed = group.Average(t => t.ThreatUnit.rb != null ? t.ThreatUnit.rb.velocity.magnitude * 3.6f : 0f);
                        float minTof = group.Min(t => GetToF(t.ThreatUnit));
                        float maxTof = group.Max(t => GetToF(t.ThreatUnit));
                        
                        string countStr = count > 1 ? $"{count}x Msles" : "Msle";
                        string seekerStr = !string.IsNullOrEmpty(group.FirstOrDefault()?.SeekerType) ? $"[{group.FirstOrDefault().SeekerType}]" : "[UNK]";
                        string tofStr = Mathf.Approximately(minTof, maxTof) ? $"{minTof:F0}s" : $"{minTof:F0}-{maxTof:F0}s";
                        
                        string msg = $"{seekerStr} [{group.Key.Sector}] {countStr} | Spd: {avgSpeed:F0}km/h | ToF: {tofStr}";
                        
                        float blinkRate = Mathf.Clamp(20f / Mathf.Max(1f, minTof), 2f, 15f);
                        bool isRed = (Time.time * blinkRate) % 1f < 0.5f;
                        GUIStyle itemStyle = new GUIStyle(incomingStyle);
                        itemStyle.normal.textColor = isRed ? Color.red : Color.yellow;

                        GUI.Box(new Rect(screenW - 370, currentY - 5, 360, 30), GUIContent.none, bgStyle);
                        GUI.Label(new Rect(screenW - 360, currentY, 350, 30), msg, itemStyle);
                        currentY += 30f;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Unit))]
    public static class UnitTracker
    {
        public static HashSet<Unit> ActiveUnits = new HashSet<Unit>();

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        static void AwakePostfix(Unit __instance)
        {
            if (__instance != null) ActiveUnits.Add(__instance);
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        static void OnDestroyPostfix(Unit __instance)
        {
            if (__instance != null) ActiveUnits.Remove(__instance);
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
