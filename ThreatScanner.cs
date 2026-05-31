using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;

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
            public string SeekerType;
        }

        public static List<ActiveThreat> activeThreats = new List<ActiveThreat>();
        private HashSet<Unit> groundNukesSpotted = new HashSet<Unit>();
        private HashSet<Unit> airbaseThreatsSpotted = new HashSet<Unit>();
        private List<Transform> _transformCache = new List<Transform>(128);
        private List<MonoBehaviour> _monoCache = new List<MonoBehaviour>(64);
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
                    
                    bool isMissile = u is Missile || u.GetType().Name == "Bomb";

                    bool hasNukeSystem = false;
                    bool targetsPlayer = false;
                    bool targetsAirbase = false;
                    string airbaseTargetName = "";
                    string targetCommandPrefix = FemboiTomboiPlugin.prefixArmy;
                    string seekerTypeStr = "";

                    if (isMissile)
                    {
                        var m = u as Missile;
                        if (m != null)
                        {
                            if (playerID.IsValid && m.targetID == playerID)
                            {
                                targetsPlayer = true;
                            }
                            else
                            {
                                // See if target is an Airbase via reflection
                                if (!_missileTargetRefInit)
                                {
                                    Type t = AccessTools.TypeByName("Missile");
                                    if (t != null) _missileTargetRef = AccessTools.FieldRefAccess<Unit>(t, "target");
                                    _missileTargetRefInit = true;
                                }
                                
                                Unit targetUnit = null;
                                if (_missileTargetRef != null) targetUnit = _missileTargetRef(m);
                                else
                                {
                                    var targetField = m.GetType().GetField("target", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (targetField != null) targetUnit = targetField.GetValue(m) as Unit;
                                }
                                
                                if (targetUnit != null && targetUnit.GetComponent<Airbase>() != null)
                                {
                                    targetsAirbase = true;
                                    airbaseTargetName = GetCleanUnitName(targetUnit);
                                    targetCommandPrefix = GetCommandPrefix(targetUnit);
                                }
                            }
                            
                            if (m.TryGetComponent(out SARHSeeker _)) seekerTypeStr = "SARH";
                            else if (m.TryGetComponent(out ARHSeeker _)) seekerTypeStr = "ARH";
                            else if (m.TryGetComponent(out IRSeeker _)) seekerTypeStr = "IR";
                            else if (m.TryGetComponent(out ARMSeeker _)) seekerTypeStr = "ARM";
                            else if (m.TryGetComponent(out OpticalSeeker _)) seekerTypeStr = "OPT";
                            else if (m.TryGetComponent(out OpticalSeekerCruiseMissile _)) seekerTypeStr = "OPT";
                            else
                            {
                                var guidField = m.GetType().GetField("guidanceMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (guidField != null)
                                {
                                    seekerTypeStr = guidField.GetValue(m).ToString();
                                }
                                else
                                {
                                    var guidProp = m.GetType().GetProperty("guidanceMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    if (guidProp != null)
                                    {
                                        seekerTypeStr = guidProp.GetValue(m, null).ToString();
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(seekerTypeStr))
                                {
                                    if (seekerTypeStr == "Radar") seekerTypeStr = "ARH";
                                    else if (seekerTypeStr == "Heat") seekerTypeStr = "IR";
                                    else if (seekerTypeStr == "AntiRad") seekerTypeStr = "ARM";
                                    else if (seekerTypeStr == "Optical") seekerTypeStr = "OPT";
                                    else if (seekerTypeStr == "Laser") seekerTypeStr = "LSR";
                                    else if (seekerTypeStr == "Semi") seekerTypeStr = "SARH";
                                    else if (seekerTypeStr == "BeamRiding") seekerTypeStr = "BEAM";
                                }
                            }
                        }
                    }

                    if (isMissile)
                    {
                        u.gameObject.GetComponents<MonoBehaviour>(_monoCache);
                        for (int i = 0; i < _monoCache.Count; i++)
                        {
                            if (CheckForNuke(_monoCache[i])) { hasNukeSystem = true; break; }
                        }
                    }
                    else if (u is Aircraft || u is GroundVehicle || u is Ship)
                    {
                        // Carrier: check children/pylons
                        u.GetComponentsInChildren<Transform>(true, _transformCache);
                        foreach (var t in _transformCache)
                        {
                            if (t == u.transform) continue;
                            t.GetComponents<MonoBehaviour>(_monoCache);
                            for (int i = 0; i < _monoCache.Count; i++)
                            {
                                if (CheckForNuke(_monoCache[i])) { hasNukeSystem = true; break; }
                            }
                            if (hasNukeSystem) break;
                        }
                    }
                    else
                    {
                        // Skip static buildings/other stuff to eliminate heavy reflection/component queries every second
                        continue;
                    }

                    if (targetsPlayer && !hasNukeSystem)
                    {
                        activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = false, IsTargetingPlayer = true, IsTargetingAirbase = false, TargetPrefix = FemboiTomboiPlugin.prefixAir, SeekerType = seekerTypeStr });
                    }
                    else if (targetsAirbase && !hasNukeSystem)
                    {
                        activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = false, IsTargetingPlayer = false, IsTargetingAirbase = true, TargetName = airbaseTargetName, TargetPrefix = targetCommandPrefix, SeekerType = seekerTypeStr });
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
                            activeThreats.Add(new ActiveThreat { ThreatUnit = u, IsLaunched = true, IsAircraft = false, IsNuke = true, IsTargetingPlayer = targetsPlayer, IsTargetingAirbase = targetsAirbase, TargetName = airbaseTargetName, TargetPrefix = targetsPlayer ? FemboiTomboiPlugin.prefixAir : (targetsAirbase ? targetCommandPrefix : FemboiTomboiPlugin.prefixArmy) });
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
        }        private Dictionary<Component, bool> _nukeCompCache = new Dictionary<Component, bool>();

        // Optimization: Cache fields per type to avoid reflection overhead every second + Helper function to dynamically check for nuclear payload
        private bool CheckForNuke(Component obj)
        {
            if (obj == null) return false;
            if (_nukeCompCache.TryGetValue(obj, out bool cached)) return cached;

            bool isNuke = false;
            try
            {
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
                        if (f.Name.IndexOf("yield", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            float val = (float)f.GetValue(obj);
                            if (val >= 1500000f) { isNuke = true; break; }
                        }
                    }
                    else if (f.Name.IndexOf("kt", StringComparison.OrdinalIgnoreCase) >= 0)
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
                                    if (wf.Name.IndexOf("yield", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        float val = (float)wf.GetValue(warheadObj);
                                        if (val >= 1500000f) { isNuke = true; break; }
                                    }
                                }
                            }
                        }
                    }
                    if (isNuke) break;
                }
            }
            catch { }

            _nukeCompCache[obj] = isNuke;
            return isNuke;
        }

        private static AccessTools.FieldRef<object, Unit> _missileTargetRef;
        private static bool _missileTargetRefInit = false;

        private float GetToF(Unit missile)
        {
            try
            {
                if (!_missileTargetRefInit)
                {
                    Type t = AccessTools.TypeByName("Missile");
                    if (t != null) _missileTargetRef = AccessTools.FieldRefAccess<Unit>(t, "target");
                    _missileTargetRefInit = true;
                }

                Unit target = null;
                if (_missileTargetRef != null)
                {
                    target = _missileTargetRef(missile);
                }
                else
                {
                    var targetField = missile.GetType().GetField("target", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (targetField != null) target = targetField.GetValue(missile) as Unit;
                }

                if (target != null)
                {
                    Vector3 mPos = missile.transform.position;
                    Vector3 tPos = target.transform.position;
                    float dist = Vector3.Distance(mPos, tPos);
                    
                    var mRb = missile.GetComponent<Rigidbody>();
                    var tRb = target.GetComponent<Rigidbody>();
                    
                    if (mRb != null && mRb.velocity.magnitude > 5f)
                    {
                        Vector3 relVel = mRb.velocity;
                        if (tRb != null) relVel -= tRb.velocity;
                        
                        Vector3 dir = (tPos - mPos).normalized;
                        float closureRate = Vector3.Dot(relVel, dir);
                        
                        if (closureRate > 5f)
                        {
                            return dist / closureRate;
                        }
                        return dist / mRb.velocity.magnitude;
                    }
                    return dist / 300f; // Assume Mach 1 ish
                }
            }
            catch { }
            return 99.9f;
        }
    }
}
