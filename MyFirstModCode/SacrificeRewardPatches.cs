using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization; // 补丁本地化需要

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

    // 1. 修复图片路径补丁
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
                // 【终极修复】必须带上 res:// 前缀，否则Godot找不到！
                path = "res://images/ui/rest_site/option_smith.png";
                Godot.GD.Print($"[MyFirstMod] 成功拦截，修正路径为: {path}");
            }
            return true; 
        }
    }

    // 2. 修复名字和描述文本补丁
    [HarmonyPatch]
    public static class LocStringPatch
    {
        [HarmonyPrepare]
        static bool Prepare() => AccessTools.TypeByName("MegaCrit.Sts2.Core.Localization.LocString") != null;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Localization.LocString"), "GetRawText");

        [HarmonyPrefix]
        public static bool Prefix(ref string __result, string key) // 假设参数是 key
        {
            if (key == null) return true;

            // 强制替换名字
            if (key.Contains("OPTION_MYFIRSTMOD_SACRIFICE_FOR_REWARD.name"))
            {
                __result = "重塑";
                return false;
            }
            // 强制替换描述
            if (key.Contains("OPTION_MYFIRSTMOD_SACRIFICE_FOR_REWARD.description") || key.Contains("MYFIRSTMOD_RESHUFFLE_DESC"))
            {
                __result = "删除两张卡，并视删除卡的价值获得一次卡牌奖励。";
                return false;
            }
            return true;
        }
    }
}