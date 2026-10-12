using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace MyFirstModCode
{
    [HarmonyPatch(typeof(RestSiteOption))]
    public static class SacrificeRewardPatches
    {
        [HarmonyPatch(nameof(RestSiteOption.Generate))]
        [HarmonyPostfix]
        public static void Postfix_Generate(Player player, ref ICollection<RestSiteOption> __result)
        {
            try
            {
                if (__result == null) __result = new List<RestSiteOption>();
                if (__result.Any(o => o.OptionId == "MYFIRSTMOD_SACRIFICE_FOR_REWARD")) return;
                __result.Add(new SacrificeForRewardOption(player));
            }
            catch (Exception ex)
            {
                Godot.GD.Print($"[MyFirstMod] 生成选项报错: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    // 1. 图片路径补丁（保留之前的成功方案）
    [HarmonyPatch]
    public static class AssetCachePatch
    {
        [HarmonyPrepare]
        static bool Prepare() => AccessTools.TypeByName("MegaCrit.Sts2.Core.Assets.AssetCache") != null;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Assets.AssetCache"), "GetTexture2D");

        [HarmonyPrefix]
        public static bool Prefix(ref string path)
        {
            if (path != null && path.Contains("option_myfirstmod_sacrifice_for_reward"))
            {
                path = "res://images/ui/rest_site/option_smith.png";
                Godot.GD.Print($"[MyFirstMod] 成功拦截，修正路径为: {path}");
            }
            return true; 
        }
    }

    // 2. 名字补丁（强制修改“重塑”）
    [HarmonyPatch]
    public static class RestSiteOptionTitlePatch
    {
        [HarmonyPrepare]
        static bool Prepare() => AccessTools.Property(typeof(RestSiteOption), "Title") != null;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => AccessTools.Property(typeof(RestSiteOption), "Title").GetGetMethod();

        [HarmonyPostfix]
        public static void Postfix(RestSiteOption __instance, ref LocString __result)
        {
            if (__instance.OptionId == "MYFIRSTMOD_SACRIFICE_FOR_REWARD")
            {
                __result = new LocString("rest_site_ui", "重塑");
            }
        }
    }
}