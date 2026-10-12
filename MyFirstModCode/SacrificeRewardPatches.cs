using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;

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

    // 图片路径补丁（保留，因为之前生效了）
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
}