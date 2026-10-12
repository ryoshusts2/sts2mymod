using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Assets; // 必须引入这个命名空间来补丁 ImageHelper

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

        // 2. 【终极修复】在底层拦截图片路径生成，把我们的选项强制指向游戏原版的“锻造”图标
        [HarmonyPatch(typeof(ImageHelper))]
        [HarmonyPatch(nameof(ImageHelper.GetImagePath))]
        [HarmonyPrefix]
        public static bool Prefix_GetImagePath(ref string __0, ref string __result)
        {
            // 如果请求的路径里包含我们 Mod 的名字，直接替换为官方图标
            if (__0 != null && __0.Contains("myfirstmod_sacrifice_for_reward"))
            {
                __result = "ui/rest_site/option_smith.png"; 
                return false; // 跳过原方法，直接返回我们设定的图标
            }
            return true;
        }
    }
}