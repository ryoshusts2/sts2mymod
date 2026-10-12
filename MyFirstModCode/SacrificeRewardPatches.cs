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

    // 终极一击：拦截 AssetCache.GetTexture2D，强制替换所有请求的错误路径
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
            // 发现游戏要加载我们的那张不存在的图片时，直接劫持
            if (path != null && path.Contains("option_myfirstmod_sacrifice_for_reward"))
            {
                // 强制篡改成官方锻造图标
                path = "ui/rest_site/option_smith.png";
                Godot.GD.Print($"[MyFirstMod] 成功拦截错误图片请求，替换为: {path}");
            }
            return true; 
        }
    }
}