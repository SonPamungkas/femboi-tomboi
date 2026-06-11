using System.Collections;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace FemboiTomboi
{
    public partial class FemboiTomboiPlugin
    {
        internal static FemboiTomboiPlugin Instance;

        internal static ConfigEntry<float> ObjHUDX;
        internal static ConfigEntry<float> ObjHUDY;

        // Returns the most recent "new objective" notification, or null if none pending.
        internal (string text, Color color) GetLatestObjectiveMessage()
        {
            if (messageLog.Count == 0) return (null, default);
            var msg = messageLog[messageLog.Count - 1];
            return (msg.Text, ColorForPriority(msg.Priority));
        }

        // Returns the closest active high-priority (Priority >= 1) objective, or null if none.
        internal (string text, Color color) GetClosestHighPriorityObjective()
        {
            if (!_displayCacheReady || !_cachedClosestHighPriority.HasValue) return (null, default);
            var obj = _cachedClosestHighPriority.Value;
            return (obj.Text, ColorForPriority(obj.Priority));
        }

        private Color ColorForPriority(int priority)
        {
            if (priority == 0) return new Color(0.1f, 0.9f, 0.2f);
            if (priority == 1) return Color.yellow;
            return (Time.time * 10f) % 1f < 0.5f ? Color.red : Color.yellow;
        }

        // Attaches the in-cockpit objective readout to every aircraft's HUD, mirroring
        // the approach used by HUDCountermeasureComp.Plugin.testFunc in HMDPlugin.cs.
        private IEnumerator AttachObjectiveHUDLoop()
        {
            ObjHUDX = Config.Bind("ObjectiveHUD", "PositionX",  20f, "Objective HUD X Position (from left edge)");
            ObjHUDY = Config.Bind("ObjectiveHUD", "PositionY", 200f, "Objective HUD Y Position (from bottom)");

            while (Encyclopedia.i == null)
            {
                yield return new WaitForSeconds(1f);
            }

            foreach (AircraftDefinition aircraftDef in Encyclopedia.i.aircraft)
            {
                Transform root = aircraftDef.aircraftParameters?.HUDExtras?.transform;
                if (root == null) continue;
                if (root.Find("ObjectiveIndicator_FemboiTomboi") != null) continue;

                // HUDExtras is a plain GameObject with no RectTransform, so a child RectTransform's
                // anchors collapse to a single point and anchoredPosition isn't in screen pixels.
                // Give it a full-stretch RectTransform matching its parent Canvas so children anchor correctly.
                if (root.GetComponent<RectTransform>() == null)
                {
                    RectTransform hudRect = root.gameObject.AddComponent<RectTransform>();
                    hudRect.anchorMin = Vector2.zero;
                    hudRect.anchorMax = Vector2.one;
                    hudRect.offsetMin = Vector2.zero;
                    hudRect.offsetMax = Vector2.zero;
                    hudRect.pivot = new Vector2(0.5f, 0.5f);
                }

                GameObject hudObj = new GameObject("ObjectiveIndicator_FemboiTomboi");
                hudObj.transform.SetParent(root, false);
                hudObj.AddComponent<ObjectiveHUDIndicator>();
            }
        }
    }

    public class ObjectiveHUDIndicator : MonoBehaviour
    {
        private Text indicatorText;

        void Awake()
        {
            RectTransform rect = gameObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(FemboiTomboiPlugin.ObjHUDX.Value, FemboiTomboiPlugin.ObjHUDY.Value);

            GameObject txtObj = new GameObject("Text");
            txtObj.transform.SetParent(transform, false);

            indicatorText = txtObj.AddComponent<Text>();
            indicatorText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            indicatorText.fontSize = 18;
            indicatorText.alignment = TextAnchor.LowerCenter;
            indicatorText.horizontalOverflow = HorizontalWrapMode.Overflow;
            indicatorText.verticalOverflow = VerticalWrapMode.Overflow;
            indicatorText.supportRichText = true;

            RectTransform txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (FemboiTomboiPlugin.Instance == null || MapTracker.IsMapOpen)
            {
                indicatorText.text = "";
                return;
            }

            var newObjective = FemboiTomboiPlugin.Instance.GetLatestObjectiveMessage();

            if (newObjective.text == null)
            {
                indicatorText.text = "";
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGBA(newObjective.color)}>{newObjective.text}</color>");

            indicatorText.text = sb.ToString();
        }
    }
}
