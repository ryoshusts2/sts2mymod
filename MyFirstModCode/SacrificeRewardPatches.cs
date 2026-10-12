using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace MyFirstModCode
{
    [HarmonyPatch(typeof(RestSiteOption))]
    public static class SacrificeRewardPatches
    {
        // 1. 在篝火生成选项时，把我们的选项加进去
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

        // 2. 【核心修复】强制将选项名字变为“重塑”，彻底解决显示为 "dig" 的问题
        [HarmonyPatch("Title", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_Title(RestSiteOption __instance, ref LocString __result)
        {
            if (__instance.OptionId == "MYFIRSTMOD_SACRIFICE_FOR_REWARD")
            {
                __result = new LocString("rest_site_ui", "重塑");
                return false;
            }
            return true;
        }

        // 3. 拦截图标路径，强制借用游戏原版的“锻造”图标，解决因为找不到图片而崩溃的问题
        [HarmonyPatch("IconPath", MethodType.Getter)]
        [HarmonyPrefix]
        public static bool Prefix_IconPath(RestSiteOption __instance, ref string __result)
        {
            if (__instance.OptionId == "MYFIRSTMOD_SACRIFICE_FOR_REWARD")
            {
                __result = "ui/rest_site/option_smith.png"; 
                return false; 
            }
            return true;
        }
    }
}