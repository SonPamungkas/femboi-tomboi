using System;
using UnityEngine;

namespace FemboiTomboi
{
    public partial class FemboiTomboiPlugin
    {
        private string GetFactionName(string objName)
        {
            objName = objName.ToLower();
            if (objName.Contains("bdf") || objName.Contains("boscali")) return "BDF";
            if (objName.Contains("pala") || objName.Contains("primeva")) return "PALA";
            return "Unknown Faction";
        }

        private string GetCommandPrefix(Unit targetUnit)
        {
            if (targetUnit == null) return FemboiTomboiPlugin.prefixArmy;
            
            string objName = targetUnit.gameObject.name.ToLower();
            string cleanName = GetCleanUnitName(targetUnit).ToLower();
            
            if (targetUnit.GetComponent("Aircraft") != null || targetUnit.GetComponent("AeroController") != null || objName.Contains("aircraft") || objName.Contains("plane") || cleanName.Contains("plane"))
                return FemboiTomboiPlugin.prefixAir;
                
            if (objName.Contains("carrier") || objName.Contains("cruiser") || objName.Contains("destroyer") || objName.Contains("ship") || objName.Contains("corvette") || cleanName.Contains("carrier") || cleanName.Contains("cruiser"))
                return FemboiTomboiPlugin.prefixNavy;
                
            return FemboiTomboiPlugin.prefixArmy;
        }

        private float? cachedGridOffsetX = null;
        private float? cachedGridOffsetY = null;
        private string  _cachedTerrainId  = null;

        private string GetCurrentTerrainId()
        {
            try
            {
                NuclearOption.SceneLoading.MapKey? mapKey = null;

                if (NuclearOption.Networking.NetworkManagerNuclearOption.i != null && NuclearOption.Networking.NetworkManagerNuclearOption.i.MapKey.HasValue)
                {
                    mapKey = NuclearOption.Networking.NetworkManagerNuclearOption.i.MapKey;
                }
                else if (MissionManager.CurrentMission != null)
                {
                    mapKey = MissionManager.CurrentMission.MapKey;
                }

                if (mapKey.HasValue)
                {
                    string path = mapKey.Value.Path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (path.IndexOf("naval", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            path.IndexOf("archipelago", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            path.IndexOf("ignus", StringComparison.OrdinalIgnoreCase) >= 0) 
                            return "terrain_naval";
                            
                        string typeName = mapKey.Value.TypeName;
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            if (typeName.IndexOf("naval", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                typeName.IndexOf("ignus", StringComparison.OrdinalIgnoreCase) >= 0)
                                return "terrain_naval";
                        }
                    }
                }
            }
            catch { }
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        private void InvalidateGridCacheIfTerrainChanged()
        {
            string id = GetCurrentTerrainId();
            if (id != _cachedTerrainId)
            {
                _cachedTerrainId  = id;
                cachedGridOffsetX = null;
                cachedGridOffsetY = null;
            }
        }

        private void SetGridOffsetsForTerrain(string terrainId)
        {
            // terrain_naval (Ignus) is 240km wide x 160km tall — 24 cols, 16 rows.
            // Standard maps (Heartland / Terrain1) are 160km x 160km — 16 cols, 16 rows.
            bool isNaval = terrainId != null &&
                           terrainId.IndexOf("naval", StringComparison.OrdinalIgnoreCase) >= 0;

            cachedGridOffsetX = isNaval ? 120000f : 80000f; // half-width in metres
            cachedGridOffsetY = 80000f;                      // half-height always 80km
        }
        
        private string GetSector(Vector3 localPosition)
        {
            // Invalidate cache whenever the active terrain changes
            InvalidateGridCacheIfTerrainChanged();

            if (cachedGridOffsetX == null || cachedGridOffsetY == null)
                SetGridOffsetsForTerrain(_cachedTerrainId);

            var globalPos = localPosition.ToGlobalPosition();

            float gridSize = 10000f; // 10 km per cell
            int xCol = Mathf.FloorToInt(((float)globalPos.x + cachedGridOffsetX.Value) / gridSize);
            int zRow = Mathf.FloorToInt((cachedGridOffsetY.Value - (float)globalPos.z) / gridSize);
            
            int maxCol = Mathf.FloorToInt((cachedGridOffsetX.Value * 2f) / gridSize) - 1;
            int maxRow = Mathf.FloorToInt((cachedGridOffsetY.Value * 2f) / gridSize) - 1;
            
            xCol = Mathf.Clamp(xCol, 0, Mathf.Max(0, maxCol));
            zRow = Mathf.Clamp(zRow, 0, Mathf.Max(0, maxRow));
            
            char letter = (char)('A' + zRow);
            return $"{letter}{xCol}";
        }

        private string GetCleanUnitName(Unit unit)
        {
            var airbase = unit.GetComponent<Airbase>();
            if (airbase != null)
            {
                var f = typeof(Airbase).GetField("airbaseName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) ??
                        typeof(Airbase).GetField("AirbaseName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) ??
                        typeof(Airbase).GetField("displayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) ??
                        typeof(Airbase).GetField("DisplayName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) ??
                        typeof(Airbase).GetField("UniqueName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        
                if (f != null && f.FieldType == typeof(string)) 
                {
                    string n = f.GetValue(airbase) as string;
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }

            string name = unit.unitName;
            if (string.IsNullOrEmpty(name)) name = unit.gameObject.name.Replace("(Clone)", "").Trim();

            name = name.Replace("_definition", "").Replace("_Definition", "");
            name = name.Replace("Aryx_", "").Replace("P_", "");
            
            name = name.Replace("Boscali", "").Replace("Primeva", "");
            name = name.Replace("BDF", "").Replace("PALA", "");

            return name.Trim();
        }

        private bool IsMapOpen()
        {
            try 
            {
                Type mapType = Type.GetType("DynamicMap, Assembly-CSharp");
                if (mapType != null)
                {
                    var mapObj = FindObjectOfType(mapType) as MonoBehaviour;
                    if (mapObj != null)
                    {
                        var mapImageField = mapType.GetField("mapImage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (mapImageField != null)
                        {
                            GameObject mapImage = mapImageField.GetValue(mapObj) as GameObject;
                            return mapImage != null && mapImage.activeInHierarchy;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
