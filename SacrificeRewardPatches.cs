using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
                File.AppendAllText(MyModEntry.LogPath, $"[{DateTime.Now}] 检测到篝火选项生成！\n");
                
                if (__result == null) __result = new List<RestSiteOption>();
                if (__result.Any(o => o.OptionId == "MYFIRSTMOD_SACRIFICE_FOR_REWARD")) return;
                __result.Add(new SacrificeForRewardOption(player));
            }
            catch (Exception ex)
            {
                try {
                    File.AppendAllText(MyModEntry.LogPath, $"[{DateTime.Now}] 补丁报错: {ex.Message}\n");
                } catch { }
            }
        }
    }
}