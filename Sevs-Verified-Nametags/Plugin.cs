using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace SevVerifiedNametags
{
    [BepInPlugin("com.sev.gorillatag.verified-nametags", "Sev Verified Nametags", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static Dictionary<string, Color> UserColors = new Dictionary<string, Color>();
        internal static Dictionary<string, string> UserLabels = new Dictionary<string, string>();

        private const string ApiUrl = "https://sevvy-wevvy.com/mods/sev-verified-nametags/api.php";

        private Harmony _harmony;

        private bool _userIdWritten = false;

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony("com.sev.gorillatag.verified-nametags");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            StartCoroutine(FetchColorsLoop());
            Logger.LogInfo("[SevVerifiedNametags] Loaded!");
        }

        private void Update()
        {
            if (_userIdWritten) return;
            try
            {
                string userId = NetworkSystem.Instance?.LocalPlayer?.UserId;
                if (!string.IsNullOrEmpty(userId))
                {
                    string path = System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "UserId.txt");
                    System.IO.File.WriteAllText(path, userId);
                    Logger.LogInfo($"[SevVerifiedNametags] Wrote user ID to {path}");
                    _userIdWritten = true;
                }
            }
            catch { }
        }

        private IEnumerator FetchColorsLoop()
        {
            using (var req = UnityWebRequest.Get(ApiUrl + "?action=get_all"))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    ParseColors(req.downloadHandler.text);
                    Logger.LogInfo($"[SevVerifiedNametags] Loaded {UserColors.Count} color entries.");
                }
            }
        }

        private static string ExtractJsonField(string obj, string key)
        {
            var m = Regex.Match(obj, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void ParseColors(string json)
        {
            try
            {
                UserColors.Clear();
                UserLabels.Clear();

                var entriesMatch = Regex.Match(json, "\"entries\"\\s*:\\s*\\[(.+?)\\]", RegexOptions.Singleline);
                if (!entriesMatch.Success) { Instance.Logger.LogWarning("[SevVerifiedNametags] ParseColors: no entries array found"); return; }

                var entryMatches = Regex.Matches(entriesMatch.Groups[1].Value, "\\{[^}]+\\}");
                foreach (Match entry in entryMatches)
                {
                    string userId = ExtractJsonField(entry.Value, "user_id");
                    string color  = ExtractJsonField(entry.Value, "color");
                    string label  = ExtractJsonField(entry.Value, "label") ?? "";

                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(color)) continue;

                    if (ColorUtility.TryParseHtmlString(color, out Color c))
                    {
                        UserColors[userId] = c;
                        UserLabels[userId] = label;
                    }
                    else
                    {
                        Instance.Logger.LogWarning($"[SevVerifiedNametags] Failed to parse color '{color}' for {userId}");
                    }
                }
            }
            catch (Exception ex) { Instance.Logger.LogError("[SevVerifiedNametags] ParseColors exception: " + ex); }
        }

        internal static string ColorToHex(Color color)
        {
            return ((int)(color.r * 255)).ToString("X2")
                 + ((int)(color.g * 255)).ToString("X2")
                 + ((int)(color.b * 255)).ToString("X2");
        }

        internal static void ApplyRigColor(VRRig rig)
        {
            try
            {
                NetPlayer creator = rig.Creator;
                if (creator == null) return;
                if (UserColors.TryGetValue(creator.UserId, out Color color))
                    ((TMPro.TMP_Text)rig.playerText1).color = color;
            }
            catch { }
        }

    }


    [HarmonyPatch(typeof(GorillaScoreBoard), "Start", MethodType.Normal)]
    internal class ScoreBoardStartPatch
    {
        private static void Prefix(GorillaScoreBoard __instance)
        {
            try
            {
                __instance.boardText.richText = true;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GorillaScoreBoard), "RedrawPlayerLines", MethodType.Normal)]
    internal class RedrawPlayerLinesPatch
    {
        private static void Postfix(GorillaScoreBoard __instance)
        {
            try
            {
                var playerIds = new List<string>();
                for (int i = 0; i < __instance.lines.Count; i++)
                {
                    var line = __instance.lines[i];
                    if (!line.IsLineActive()) continue;
                    if (!line.IsPlayerInRoom()) continue;
                    playerIds.Add(line.linePlayer?.UserId ?? "");
                }

                if (playerIds.Count == 0) return;

                string[] parts = ((TMPro.TMP_Text)__instance.boardText).text.Split('\n');
                var sb = new StringBuilder();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) sb.Append('\n');
                    int pidx = i - 2;
                    if (pidx >= 0 && pidx < playerIds.Count)
                    {
                        string userId = playerIds[pidx];
                        string plain = Regex.Replace(parts[i], "<[^>]+>", "").TrimStart(' ');
                        sb.Append(' ');
                        if (!string.IsNullOrEmpty(userId) && Plugin.UserColors.TryGetValue(userId, out Color c))
                            sb.Append("<color=#").Append(Plugin.ColorToHex(c)).Append(">").Append(plain).Append("</color>");
                        else
                            sb.Append(parts[i].TrimStart(' '));
                    }
                    else
                    {
                        sb.Append(parts[i]);
                    }
                }
                ((TMPro.TMP_Text)__instance.boardText).text = sb.ToString();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(VRRig), "SerializeReadShared")]
    internal class VRRigSerializeReadSharedPatch
    {
        private static void Postfix(VRRig __instance)
        {
            Plugin.ApplyRigColor(__instance);
        }
    }

    [HarmonyPatch(typeof(VRRig), "UpdateName", new[] { typeof(bool) })]
    internal class VRRigUpdateNamePatch
    {
        private static void Postfix(VRRig __instance)
        {
            Plugin.ApplyRigColor(__instance);
        }
    }
}
