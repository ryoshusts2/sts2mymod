using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;

namespace MyFirstModCode
{
    // 1. 在篝火生成选项时，把我们的选项加进去
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

    // 2. 用字符串反射拦截底层图片加载，强制指向游戏自带的“锻造”图标
    [HarmonyPatch]
    public static class ImageHelperPatch
    {
        [HarmonyPrepare]
        static bool Prepare() => AccessTools.TypeByName("MegaCrit.Sts2.Core.Assets.ImageHelper") != null;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Assets.ImageHelper"), "GetImagePath");

        [HarmonyPrefix]
        public static bool Prefix(ref string __0, ref string __result)
        {
            // 如果游戏试图加载我们的选项图标，直接替换为官方的锻造图标
            if (__0 != null && __0.Contains("myfirstmod_sacrifice_for_reward"))
            {
                __result = "ui/rest_site/option_smith.png"; 
                return false; // 跳过原方法，直接返回我们设定的图标
            }
            return true;
        }
    }
}