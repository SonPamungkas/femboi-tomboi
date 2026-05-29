using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FemboiTomboi
{
    public partial class FemboiTomboiPlugin
    {
        private class TacticalMessage
        {
            public string Text;
            public float Timestamp;
            public float ExpirationTime;
            public int Priority;
        }

        private List<TacticalMessage> messageLog = new List<TacticalMessage>();
        private List<TacticalMessage> activeObjectives = new List<TacticalMessage>();
        private HashSet<Unit> previouslySpottedAircraft = new HashSet<Unit>();
        private HashSet<Unit> previouslySpottedAirDefenses = new HashSet<Unit>();
        private HashSet<Unit> previouslySpottedCAS = new HashSet<Unit>();
        private HashSet<Unit> previouslySpottedIntercept = new HashSet<Unit>();

        private Airbase[] cachedAirbases = null;

        private Dictionary<Unit, float> pendingSpotted = new Dictionary<Unit, float>();
        private Dictionary<Unit, float> pendingSpottedAirDefenses = new Dictionary<Unit, float>();
        private Dictionary<Unit, float> pendingSpottedCAS = new Dictionary<Unit, float>();
        private Dictionary<Unit, float> pendingSpottedIntercept = new Dictionary<Unit, float>();

        private bool IsSEADTarget(Unit selected)
        {
            bool isSEAD = false;
            var defField = selected.GetType().GetField("definition", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (defField == null) defField = selected.GetType().BaseType?.GetField("definition", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (defField != null)
            {
                var def = defField.GetValue(selected);
                if (def != null)
                {
                    var typeIdField = def.GetType().GetField("typeIdentity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var roleIdField = def.GetType().GetField("roleIdentity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (typeIdField != null && roleIdField != null)
                    {
                        var typeId = typeIdField.GetValue(def);
                        var roleId = roleIdField.GetValue(def);
                        if (typeId != null && roleId != null)
                        {
                            var radarField = typeId.GetType().GetField("radar", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            var antiMissileField = roleId.GetType().GetField("antiMissile", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (radarField != null && antiMissileField != null)
                            {
                                float rScore = (float)radarField.GetValue(typeId);
                                float amScore = (float)antiMissileField.GetValue(roleId);
                                if (rScore > 0f || amScore > 0f) isSEAD = true;
                            }
                        }
                    }
                }
            }
            
            string n = selected.gameObject.name.ToLower();
            isSEAD = isSEAD || n.Contains("sam") || n.Contains("radar") || n.Contains("cram") || n.Contains("lads") || n.Contains("spaag") || n.Contains("_aa") || n.EndsWith(" aa") || n.Contains("-aa");
            
            return isSEAD;
        }

        private IEnumerator DatalinkSpotterLoop()
        {
            yield return new WaitForSeconds(5f);

            while (true)
            {
                yield return new WaitForSeconds(1f);

                GameManager.GetLocalAircraft(out Aircraft localAc);
                if (localAc == null || localAc.NetworkHQ == null) continue;

                var playerHQ = localAc.NetworkHQ;

                foreach (var u in UnitTracker.ActiveUnits)
                {
                    if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                    
                    if (u is Aircraft && u.NetworkHQ != null && u.NetworkHQ != playerHQ)
                    {
                        if (playerHQ.IsTargetPositionAccurate(u, 20f))
                        {
                            bool nearFriendlyGround = UnitTracker.ActiveUnits.Any(f => f != null && f.gameObject.activeInHierarchy && !f.disabled && (f is GroundVehicle || f is Building) && f.NetworkHQ == playerHQ && Vector3.Distance(f.transform.position, u.transform.position) < 15000f);
                            if (nearFriendlyGround)
                            {
                                if (!previouslySpottedIntercept.Contains(u) && !pendingSpottedIntercept.ContainsKey(u))
                                {
                                    pendingSpottedIntercept[u] = Time.time;
                                }
                            }
                            else
                            {
                                if (!previouslySpottedAircraft.Contains(u) && !pendingSpotted.ContainsKey(u))
                                {
                                    pendingSpotted[u] = Time.time;
                                }
                            }
                        }
                    }
                    
                    else if ((u is GroundVehicle || u is Building) && u.NetworkHQ != null && u.NetworkHQ != playerHQ)
                    {
                        bool isAD = IsSEADTarget(u);
                        if (isAD)
                        {
                            if (!previouslySpottedAirDefenses.Contains(u) && !pendingSpottedAirDefenses.ContainsKey(u))
                            {
                                if (playerHQ.IsTargetPositionAccurate(u, 20f))
                                {
                                    pendingSpottedAirDefenses[u] = Time.time;
                                }
                            }
                        }
                        else
                        {
                            bool nearFriendlyGround = UnitTracker.ActiveUnits.Any(f => f != null && f.gameObject.activeInHierarchy && !f.disabled && (f is GroundVehicle || f is Building) && f.NetworkHQ == playerHQ && Vector3.Distance(f.transform.position, u.transform.position) < 8000f);
                            if (nearFriendlyGround)
                            {
                                if (!previouslySpottedCAS.Contains(u) && !pendingSpottedCAS.ContainsKey(u))
                                {
                                    if (playerHQ.IsTargetPositionAccurate(u, 20f))
                                    {
                                        pendingSpottedCAS[u] = Time.time;
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (pendingSpotted.Count > 0)
                {
                    float now = Time.time;
                    float newestSpot = pendingSpotted.Values.Max();
                    float oldestSpot = pendingSpotted.Values.Min();

                    if (now - newestSpot > 3f || now - oldestSpot > 8f)
                    {
                        var newSpotted = pendingSpotted.Keys.ToList();
                        var groupedBySector = newSpotted.GroupBy(u => GetSector(u.transform.position));
                        foreach (var sectorGroup in groupedBySector)
                        {
                            var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                            System.Collections.Generic.List<string> targetStrings = new System.Collections.Generic.List<string>();
                            foreach (var nameGroup in groupedByName)
                            {
                                int count = nameGroup.Count();
                                if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                                else targetStrings.Add(nameGroup.Key);
                            }
                            
                            string targetDesc = string.Join(" ; ", targetStrings);
                            string locationStr = "Sector " + sectorGroup.Key;
                            string msg = $"{FemboiTomboiPlugin.prefixAir}:\nNew Tasking: CAP\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                            ShowCommanderMessage(msg, 18f);
                        }

                        foreach (var u in newSpotted) previouslySpottedAircraft.Add(u);
                        pendingSpotted.Clear();
                    }
                }
                
                if (pendingSpottedAirDefenses.Count > 0)
                {
                    float now = Time.time;
                    float newestSpot = pendingSpottedAirDefenses.Values.Max();
                    float oldestSpot = pendingSpottedAirDefenses.Values.Min();

                    if (now - newestSpot > 3f || now - oldestSpot > 8f)
                    {
                        var newSpotted = pendingSpottedAirDefenses.Keys.ToList();
                        var groupedBySector = newSpotted.GroupBy(u => GetSector(u.transform.position));
                        foreach (var sectorGroup in groupedBySector)
                        {
                            var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                            System.Collections.Generic.List<string> targetStrings = new System.Collections.Generic.List<string>();
                            foreach (var nameGroup in groupedByName)
                            {
                                int count = nameGroup.Count();
                                if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                                else targetStrings.Add(nameGroup.Key);
                            }
                            
                            string targetDesc = string.Join(" ; ", targetStrings);
                            string locationStr = "Sector " + sectorGroup.Key;
                            string msg = $"{FemboiTomboiPlugin.prefixAir}:\nNew Tasking: SEAD/DEAD\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                            ShowCommanderMessage(msg, 18f);
                        }

                        foreach (var u in newSpotted) previouslySpottedAirDefenses.Add(u);
                        pendingSpottedAirDefenses.Clear();
                    }
                }
                
                if (pendingSpottedCAS.Count > 0)
                {
                    float now = Time.time;
                    float newestSpot = pendingSpottedCAS.Values.Max();
                    float oldestSpot = pendingSpottedCAS.Values.Min();

                    if (now - newestSpot > 3f || now - oldestSpot > 8f)
                    {
                        var newSpotted = pendingSpottedCAS.Keys.ToList();
                        var groupedBySector = newSpotted.GroupBy(u => GetSector(u.transform.position));
                        foreach (var sectorGroup in groupedBySector)
                        {
                            var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                            System.Collections.Generic.List<string> targetStrings = new System.Collections.Generic.List<string>();
                            foreach (var nameGroup in groupedByName)
                            {
                                int count = nameGroup.Count();
                                if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                                else targetStrings.Add(nameGroup.Key);
                            }
                            
                            string targetDesc = string.Join(" ; ", targetStrings);
                            string locationStr = "Sector " + sectorGroup.Key;
                            string msg = $"{FemboiTomboiPlugin.prefixArmy}:\nRequesting CAS!\nGround units engaged by: {targetDesc} at {locationStr}.\nRequire immediate support.";
                            ShowCommanderMessage(msg, 20f, 1);
                        }

                        foreach (var u in newSpotted) previouslySpottedCAS.Add(u);
                        pendingSpottedCAS.Clear();
                    }
                }

                if (pendingSpottedIntercept.Count > 0)
                {
                    float now = Time.time;
                    float newestSpot = pendingSpottedIntercept.Values.Max();
                    float oldestSpot = pendingSpottedIntercept.Values.Min();

                    if (now - newestSpot > 3f || now - oldestSpot > 8f)
                    {
                        var newSpotted = pendingSpottedIntercept.Keys.ToList();
                        var groupedBySector = newSpotted.GroupBy(u => GetSector(u.transform.position));
                        foreach (var sectorGroup in groupedBySector)
                        {
                            var groupedByName = sectorGroup.GroupBy(u => GetCleanUnitName(u));
                            System.Collections.Generic.List<string> targetStrings = new System.Collections.Generic.List<string>();
                            foreach (var nameGroup in groupedByName)
                            {
                                int count = nameGroup.Count();
                                if (count > 1) targetStrings.Add($"{count}x {nameGroup.Key}");
                                else targetStrings.Add(nameGroup.Key);
                            }
                            
                            string targetDesc = string.Join(" ; ", targetStrings);
                            string locationStr = "Sector " + sectorGroup.Key;
                            string msg = $"{FemboiTomboiPlugin.prefixArmy}:\nRequesting INTERCEPT!\nEnemy aircraft attacking friendly ground units: {targetDesc} at {locationStr}.";
                            ShowCommanderMessage(msg, 20f, 1);
                        }

                        foreach (var u in newSpotted) previouslySpottedIntercept.Add(u);
                        pendingSpottedIntercept.Clear();
                    }
                }

                // Cleanup destroyed units
                previouslySpottedAircraft.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy || u.disabled);
                previouslySpottedAirDefenses.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy || u.disabled);
                previouslySpottedCAS.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy || u.disabled);
                previouslySpottedIntercept.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy || u.disabled);
                
                var deadPending = pendingSpotted.Keys.Where(u => u == null || !u.gameObject.activeInHierarchy || u.disabled).ToList();
                foreach (var d in deadPending) pendingSpotted.Remove(d);
                
                var deadPendingAD = pendingSpottedAirDefenses.Keys.Where(u => u == null || !u.gameObject.activeInHierarchy || u.disabled).ToList();
                foreach (var d in deadPendingAD) pendingSpottedAirDefenses.Remove(d);
                
                var deadPendingCAS = pendingSpottedCAS.Keys.Where(u => u == null || !u.gameObject.activeInHierarchy || u.disabled).ToList();
                foreach (var d in deadPendingCAS) pendingSpottedCAS.Remove(d);

                var deadPendingInt = pendingSpottedIntercept.Keys.Where(u => u == null || !u.gameObject.activeInHierarchy || u.disabled).ToList();
                foreach (var d in deadPendingInt) pendingSpottedIntercept.Remove(d);
            }
        }

        private void ShowCommanderMessage(string msg, float duration, int priority = 0)
        {
            messageLog.Add(new TacticalMessage { Text = msg, Timestamp = Time.time, ExpirationTime = Time.time + duration, Priority = priority });
            if (messageLog.Count > 1)
            {
                messageLog.RemoveAt(0); // Keep max 1 new message in the feed
            }

            Logger.LogInfo(msg);
        }

        private bool NeedsResupply(Unit u)
        {
            if (u == null || !u.gameObject.activeInHierarchy) return false;

            int currentMissiles = 0;
            int maxMissiles = 0;

            try
            {
                var wm = u.GetComponentInChildren<WeaponManager>();
                if (wm != null)
                {
                    currentMissiles += wm.GetCurrentWarheads();
                    maxMissiles += 1;
                }
            }
            catch { }

            var components = u.GetComponentsInChildren<Component>();
            foreach (var c in components)
            {
                if (c == null) continue;
                string typeName = c.GetType().Name;
                
                // Only care about Launchers and WeaponStations (Missiles), ignore Guns/Turrets
                if (typeName == "Launcher" || typeName == "WeaponStation")
                {
                    try
                    {
                        var type = c.GetType();
                        var aField = type.GetField("ammo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var mField = type.GetField("maxAmmo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (aField != null) currentMissiles += (int)aField.GetValue(c);
                        if (mField != null) maxMissiles += (int)mField.GetValue(c);
                        else if (aField != null) maxMissiles += 1;
                    }
                    catch { }
                }
            }

            if (maxMissiles > 0)
            {
                // Trigger resupply if missiles are low (<= 25%) or completely empty
                return currentMissiles <= Mathf.CeilToInt(maxMissiles * 0.25f);
            }
            
            return false;
        }

        private IEnumerator CommanderLoop()
        {
            // Wait for game to initialize
            yield return new WaitForSeconds(15f);

            while (true)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(45f, 90f));

                // Prevent new tasking if an aircraft carrier is persistent
                if (activeThreats.Any(t => t.IsAircraft))
                {
                    continue;
                }

                string missionType = "Patrol";
                string targetDesc = "Unknown Contacts";
                string targetFaction = "Enemy";
                
                // Real sector assignment
                string locationStr = "General Patrol";

                try
                {
                    var potentialTargets = new List<Unit>();
                    var resupplyTargets = new List<Unit>();
                    
                    GameManager.GetLocalAircraft(out Aircraft localAc);
                    var playerHQ = localAc != null ? localAc.NetworkHQ : null;

                    foreach (var u in UnitTracker.ActiveUnits)
                    {
                        if (u == null || !u.gameObject.activeInHierarchy || u.disabled) continue;
                        if (u is Missile || u.gameObject.GetComponent("Bomb") != null) continue;
                        
                        string name = u.gameObject.name;
                        if (string.IsNullOrEmpty(name)) continue;

                        if (playerHQ != null && u.NetworkHQ != null)
                        {
                            if (u.NetworkHQ != playerHQ)
                            {
                                // Enemy
                                potentialTargets.Add(u);
                            }
                            else
                            {
                                // Friendly
                                if (!(u is Aircraft)) // Usually don't resupply flying aircraft this way
                                {
                                    if (NeedsResupply(u))
                                    {
                                        resupplyTargets.Add(u);
                                    }
                                }
                            }
                        }
                    }

                    if (resupplyTargets.Count > 0 && UnityEngine.Random.value > 0.4f)
                    {
                        missionType = "Resupply";
                        var ships = resupplyTargets.Where(u => !(u is Aircraft) && !(u is GroundVehicle) && !(u is Building)).ToList();
                        
                        if (ships.Count > 0)
                        {
                            var targetShip = ships[UnityEngine.Random.Range(0, ships.Count)];
                            targetDesc = GetCleanUnitName(targetShip);
                            locationStr = "Sector " + GetSector(targetShip.transform.position);
                            
                            string pfx = FemboiTomboiPlugin.prefixNavy;
                            string rMsg = $"{pfx}:\nNew Tasking: {missionType}\nTarget: {targetDesc} requires immediate resupply at {locationStr}.";
                            ShowCommanderMessage(rMsg, 18f);
                            continue;
                        }
                        else
                        {
                            var groundUnits = resupplyTargets.Except(ships).ToList();
                            if (groundUnits.Count > 0)
                            {
                                var sectorGroups = groundUnits.GroupBy(u => GetSector(u.transform.position)).ToList();
                                var selectedGroup = sectorGroups[UnityEngine.Random.Range(0, sectorGroups.Count)];
                                
                                int count = selectedGroup.Count();
                                targetDesc = $"{count} Ground Unit{(count > 1 ? "s" : "")}";
                                locationStr = "Sector " + selectedGroup.Key;
                                
                                string pfx = FemboiTomboiPlugin.prefixNavy;
                                string rMsg = $"{pfx}:\nNew Tasking: {missionType}\nTarget: {targetDesc} require immediate resupply at {locationStr}.";
                                ShowCommanderMessage(rMsg, 18f);
                                continue;
                            }
                        }
                    }

                    if (potentialTargets.Count > 0)
                    {
                        Unit selected = potentialTargets[UnityEngine.Random.Range(0, potentialTargets.Count)];
                        string name = selected.gameObject.name;
                        
                        targetDesc = GetCleanUnitName(selected);
                        string fac = GetFactionName(name);
                        if (fac != "Unknown Faction") targetFaction = fac;
                        
                        locationStr = "Sector " + GetSector(selected.transform.position);

                        if (!(selected is Aircraft) && !(selected is GroundVehicle) && !(selected is Building))
                        {
                            missionType = "Maritime Strike";
                        }
                        else if (!(selected is Aircraft))
                        {
                            bool isSEAD = IsSEADTarget(selected);
                            string tDesc = targetDesc.ToLower();
                            isSEAD = isSEAD || tDesc.Contains("sam") || tDesc.Contains("radar") || tDesc.Contains("cram") || tDesc.Contains("lads") || tDesc.Contains("spaag") || tDesc.Contains(" aa ") || tDesc.EndsWith(" aa");

                            if (isSEAD)
                            {
                                missionType = "SEAD/DEAD";
                            }
                            else
                            {
                                missionType = "CAS";
                            }
                        }
                        else if (selected is Aircraft)
                        {
                            missionType = "Interception";
                        }
                        else
                        {
                            string[] randomMissions = { "Strike Mission", "Air Support", };
                            missionType = randomMissions[UnityEngine.Random.Range(0, randomMissions.Length)];
                        }
                    }
                    else
                    {
                        string[] randomMissions = { "CAP", "Airborne Patrol", "Recon Patrol" };
                        missionType = randomMissions[UnityEngine.Random.Range(0, randomMissions.Length)];
                        targetDesc = "Maintain Airspace";
                        targetFaction = "N/A";
                        
                        if (cachedAirbases == null || cachedAirbases.Length == 0) cachedAirbases = UnityEngine.Object.FindObjectsOfType<Airbase>();
                        var airbases = cachedAirbases;
                        if (airbases != null && airbases.Length > 0)
                        {
                            var randAirbase = airbases[UnityEngine.Random.Range(0, airbases.Length)];
                            locationStr = "Sector " + GetSector(randAirbase.transform.position);
                        }
                        else
                        {
                            var allUnits = UnitTracker.ActiveUnits.Where(u => u != null && u.gameObject.activeInHierarchy && !u.disabled).ToList();
                            if (allUnits.Count > 0)
                            {
                                var randUnit = allUnits[UnityEngine.Random.Range(0, allUnits.Count)];
                                locationStr = "Sector " + GetSector(randUnit.transform.position);
                            }
                            else
                            {
                                locationStr = "General Patrol";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Battlefield analysis failed, using fallback targets. Error: " + ex.Message);
                }

                string prefix = FemboiTomboiPlugin.prefixArmy;
                if (missionType == "SEAD/DEAD" || missionType == "Strike") prefix = FemboiTomboiPlugin.prefixAir;
                else if (missionType == "Interception" || missionType == "CAP" || missionType == "Airborne Patrol" || missionType == "Recon Patrol") prefix = FemboiTomboiPlugin.prefixAir;
                else if (missionType == "Maritime Strike" || missionType == "Resupply") prefix = FemboiTomboiPlugin.prefixNavy;

                string factionLine = targetFaction != "Enemy" && targetFaction != "N/A" ? $" ({targetFaction})" : "";
                string fullMessage = $"{prefix}:\nNew Tasking: {missionType}\nTarget: {targetDesc}{factionLine} at {locationStr}\nExecute when ready.";

                ShowCommanderMessage(fullMessage, 18f);
            }
        }
    }
}
