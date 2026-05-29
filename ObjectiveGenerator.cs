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

        private Dictionary<Unit, float> pendingSpotted = new Dictionary<Unit, float>();

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
                        if (!previouslySpottedAircraft.Contains(u) && !pendingSpotted.ContainsKey(u))
                        {
                            if (playerHQ.IsTargetPositionAccurate(u, 20f))
                            {
                                pendingSpotted[u] = Time.time;
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
                            string msg = $"MOMMY:\nNew Tasking: CAP\nTarget: {targetDesc} at {locationStr}.\nExecute when ready.";
                            ShowCommanderMessage(msg, 18f);
                        }

                        foreach (var u in newSpotted) previouslySpottedAircraft.Add(u);
                        pendingSpotted.Clear();
                    }
                }

                // Cleanup destroyed aircraft
                previouslySpottedAircraft.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy || u.disabled);
                
                var deadPending = pendingSpotted.Keys.Where(u => u == null || !u.gameObject.activeInHierarchy || u.disabled).ToList();
                foreach (var d in deadPending) pendingSpotted.Remove(d);
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

            string[] prefixes = { "FEMBOI", "TOMBOI", "MOMMY" };
            
            while (true)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(45f, 90f));

                // Prevent new tasking if an aircraft carrier is persistent
                if (activeThreats.Any(t => t.IsAircraft))
                {
                    continue;
                }

                string prefix = prefixes[UnityEngine.Random.Range(0, prefixes.Length)];
                string missionType = "Patrol";
                string targetDesc = "Unknown Contacts";
                string targetFaction = "Enemy";
                
                // Real sector assignment
                string locationStr = "Sector Unknown";

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
                            
                            string rMsg = $"{prefix}:\nNew Tasking: {missionType}\nTarget: {targetDesc} requires immediate resupply at {locationStr}.";
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
                                
                                string rMsg = $"{prefix}:\nNew Tasking: {missionType}\nTarget: {targetDesc} require immediate resupply at {locationStr}.";
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
                        else if (selected is GroundVehicle)
                        {
                            // Check if it has any launcher
                            bool hasLauncher = selected.GetComponentsInChildren<Component>().Any(c => c != null && (c.GetType().Name == "Launcher" || c.GetType().Name == "WeaponStation"));
                            if (hasLauncher || selected.gameObject.name.IndexOf("sam", StringComparison.OrdinalIgnoreCase) >= 0)
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
                            string[] randomMissions = { "Strike", "Ground Support", "Air Interdiction", "Recon" };
                            missionType = randomMissions[UnityEngine.Random.Range(0, randomMissions.Length)];
                        }
                    }
                    else
                    {
                        string[] randomMissions = { "CAP", "Airborne Patrol" };
                        missionType = randomMissions[UnityEngine.Random.Range(0, randomMissions.Length)];
                        targetDesc = "Maintain Airspace";
                        targetFaction = "N/A";
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Battlefield analysis failed, using fallback targets. Error: " + ex.Message);
                }

                string factionLine = targetFaction != "Enemy" && targetFaction != "N/A" ? $" ({targetFaction})" : "";
                string fullMessage = $"{prefix}:\nNew Tasking: {missionType}\nTarget: {targetDesc}{factionLine} at {locationStr}\nExecute when ready.";

                ShowCommanderMessage(fullMessage, 18f);
            }
        }
    }
}
