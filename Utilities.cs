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
            if (targetUnit == null) return "TOMBOI";
            
            string objName = targetUnit.gameObject.name.ToLower();
            string cleanName = GetCleanUnitName(targetUnit).ToLower();
            
            if (targetUnit.GetComponent("Aircraft") != null || targetUnit.GetComponent("AeroController") != null || objName.Contains("aircraft") || objName.Contains("plane") || cleanName.Contains("plane"))
                return "MOMMY Command";
                
            if (objName.Contains("carrier") || objName.Contains("cruiser") || objName.Contains("destroyer") || objName.Contains("ship") || objName.Contains("corvette") || cleanName.Contains("carrier") || cleanName.Contains("cruiser"))
                return "FEMBOI Admiral";
                
            return "TOMBOI General";
        }

        private float? cachedGridOffsetX = null;
        private float? cachedGridOffsetY = null;
        
        private string GetSector(Vector3 localPosition)
        {
            if (cachedGridOffsetX == null || cachedGridOffsetY == null)
            {
                cachedGridOffsetX = 80000f;
                cachedGridOffsetY = 80000f;
                try
                {
                    Type glType = Type.GetType("GridLabels, Assembly-CSharp");
                    if (glType != null)
                    {
                        var obj = FindObjectOfType(glType);
                        if (obj != null)
                        {
                            var fX = glType.GetField("offsetX", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var fY = glType.GetField("offsetY", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (fX != null) cachedGridOffsetX = (float)fX.GetValue(obj);
                            if (fY != null) cachedGridOffsetY = (float)fY.GetValue(obj);
                        }
                    }
                }
                catch { }
            }

            var globalPos = localPosition.ToGlobalPosition();

            float gridSize = 10000f; // 10km grids
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
