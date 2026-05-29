using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FemboiTomboi
{
    public partial class FemboiTomboiPlugin
    {
        public class ActiveThreat
        {
            public Unit ThreatUnit;
            public bool IsLaunched;
            public bool IsAircraft;
            public bool IsNuke;
            public bool IsTargetingPlayer;
            public bool IsTargetingAirbase;
            public string TargetName;
            public string TargetPrefix;
        }

        public static List<ActiveThreat> activeThreats = new List<ActiveThreat>();
        private HashSet<Unit> groundNukesSpotted = new HashSet<Unit>();
        private HashSet<Unit> airbaseThreatsSpotted = new HashSet<Unit>();
        private List<Transform> _transformCache = new List<Transform>(128);
        private static Dictionary<Type, System.Reflection.FieldInfo[]> femboiTomboiNukeTypeCache = new Dictionary<Type, System.Reflection.FieldInfo[]>();

        private IEnumerator NukeScannerLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                
                activeThreats.Clear();

                PersistentID playerID = default;
                try
                {
                    if (GameManager.GetLocalAircraft(out Aircraft localAc) && localAc != null)
                    {
                        playerID = localAc.persistentID;
                    }
                }
                catch { }
                
                foreach (var u in UnitTracker.ActiveUnits)
                {
                    if (u == null || !u.gameObject.activeInHierarchy) continue;
                    
                    bool isMissile = u.gameObject.GetComponent("Missile") != null || u.gameObject.GetComponent("Bomb") != null;
                    bool hasNukeSystem = false;
                    bool targetsPlayer = false;
                    bool targetsAirbase = false;
                    string airbaseTargetName = "";
                    string targetCommandPrefix = "TOMBOI";

                    if (isMissile)
                    {
                        var m = u.gameObject.GetComponent("Missile") as Missile;
                        if (m != null)
                        {
                            if (playerID.IsValid && m.targetID == playerID)
                            {
                                targetsPlayer = true;
                            }
                            else
                            {
                                // See if target is an Airbase via reflection (since we know the 'target' field exists)
                                var targetField = m.GetType().GetField("target", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (targetField != null)
                                {
                                    var targetUnit = targetField.GetValue(m) as Unit;
                                    if (targetUnit != null && targetUnit.GetComponent<Airbase>() != null)
                                    {
                                        targetsAirbase = true;
                                        airbaseTargetName = GetCleanUnitName(targetUnit);
                                        targetCommandPrefix = GetCommandPrefix(targetUnit);
                                    }
                                }
                            }
                        }
                    }

                    // Optimization: Cache fields per type to avoid reflection overhead every second + Helper function to dynamically check for nuclear payload
                    bool CheckForNuke(object obj)
                    {
                        if (obj == null) return false;
                        Type objType = obj.GetType();
                        
                        System.Reflection.FieldInfo[] fields;
                        if (!femboiTomboiNukeTypeCache.TryGetValue(objType, out fields))
                        {
                            fields = objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            femboiTomboiNukeTypeCache[objType] = fields;
                        }
                        
                        foreach (var f in fields)
                        {
                            if (f.FieldType == typeof(float))
                            {
                                if (f.Name.IndexOf("yield", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                    f.Name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    float val = (float)f.GetValue(obj);
                                    if (val >= 1500f) return true;
                                }
                            }
                            else if (f.Name.IndexOf("warhead", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                object warheadObj = f.GetValue(obj);
                                if (warheadObj != null)
                                {
                                    Type wType = warheadObj.GetType();
                                    System.Reflection.FieldInfo[] wFields;
                                    if (!femboiTomboiNukeTypeCache.TryGetValue(wType, out wFields))
                                    {
                                        wFields = wType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        femboiTomboiNukeTypeCache[wType] = wFields;
                                    }
                                    
                                    foreach (var wf in wFields)
                                    {
                                        if (wf.FieldType == typeof(float))
                                        {
                                            if (wf.Name.IndexOf("yield", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                wf.Name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                float val = (float)wf.GetValue(warheadObj);
                                                if (val >= 1500f) return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        return false;
                    }

                    if (isMissile)
                    {
                        // Check the missile/bomb components themselves
                        foreach (var comp in u.gameObject.GetComponents<MonoBehaviour>())
                        {
                            if (CheckForNuke(comp)) { hasNukeSystem = true; break; }
                        }
                    }
                    else
                    {
                        // Carrier: check children/pylons
                        u.GetComponentsInChildren<Transform>(true, _transformCache);
                        foreach (var t in _transformCache)
                        {
                            if (t == u.transform) continue;
                            foreach (var comp in t.GetComponents<MonoBehaviour>())
                            {
                                if (CheckForNuke(comp)) { hasNukeSystem = true; break; }
                            }
                            if (hasNukeSystem) break;
                        }
                    }

                    if (targetsPlayer && !hasNukeSystem)
                    {
                        activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = false, IsTargetingPlayer = true, IsTargetingAirbase = false, TargetPrefix = "MOMMY" });
                    }
                    else if (targetsAirbase && !hasNukeSystem)
                    {
                        activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = false, IsTargetingPlayer = false, IsTargetingAirbase = true, TargetName = airbaseTargetName, TargetPrefix = targetCommandPrefix });
                        if (!airbaseThreatsSpotted.Contains(u))
                        {
                            airbaseThreatsSpotted.Add(u);
                            ShowCommanderMessage($"{targetCommandPrefix}: WARNING: Missile strike detected targeting {airbaseTargetName} in {GetSector(u.transform.position)}!", 20f, 1);
                        }
                    }
                    else if (hasNukeSystem)
                    {
                        if (u.transform.parent == null && isMissile) // Launched Nuke
                        {
                            activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = true, IsTargetingPlayer = targetsPlayer, IsTargetingAirbase = targetsAirbase, TargetName = airbaseTargetName, TargetPrefix = targetsPlayer ? "MOMMY" : (targetsAirbase ? targetCommandPrefix : "TOMBOI") });
                            if (targetsAirbase && !airbaseThreatsSpotted.Contains(u))
                            {
                                airbaseThreatsSpotted.Add(u);
                                ShowCommanderMessage($"{targetCommandPrefix}: CRITICAL: Nuclear strike detected targeting {airbaseTargetName} in {GetSector(u.transform.position)}!", 20f, 2);
                            }
                        }
                        else
                        {
                            bool isAircraft = u is Aircraft;
                                              
                            if (isAircraft)
                            {
                                activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = false, IsAircraft = true, IsNuke = true, IsTargetingPlayer = false });
                            }
                            else
                            {
                                if (!groundNukesSpotted.Contains(u))
                                {
                                    groundNukesSpotted.Add(u);
                                    string fac = GetFactionName(u.gameObject.name);
                                    string facPrefix = fac != "Unknown Faction" ? fac + " " : "";
                                    ShowCommanderMessage($"WARNING: {facPrefix}{GetCleanUnitName(u)} detected with Nuclear Payload in {GetSector(u.transform.position)}!", 20f, 2);
                                }
                            }
                        }
                    }
                }
                groundNukesSpotted.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy);
                airbaseThreatsSpotted.RemoveWhere(u => u == null || !u.gameObject.activeInHierarchy);
            }
        }

        private float GetToF(Unit missile)
        {
            try
            {
                var type = missile.GetType();
                var targetField = type.GetField("target", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (targetField != null)
                {
                    var target = targetField.GetValue(missile) as Unit;
                    if (target != null)
                    {
                        float dist = Vector3.Distance(missile.transform.position, target.transform.position);
                        var rb = missile.GetComponent<Rigidbody>();
                        if (rb != null && rb.velocity.magnitude > 5f)
                        {
                            return dist / rb.velocity.magnitude;
                        }
                        return dist / 300f; // Assume Mach 1 ish
                    }
                }
            }
            catch { }
            return 99.9f;
        }
    }
}
