using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace MyFirstModCode
{
    public class SacrificeForRewardOption : RestSiteOption
    {
        public SacrificeForRewardOption(Player owner) : base(owner) { }
        public override string OptionId => "MYFIRSTMOD_SACRIFICE_FOR_REWARD";
        public override LocString Description => new LocString("rest_site_ui", "MYFIRSTMOD_RESHUFFLE_DESC");

        public override bool IsEnabled
        {
            get { return Owner?.Deck != null && Owner.Deck.Cards.Count(IsValidCard) >= 2; }
        }

        public override async Task<bool> OnSelect()
        {
            if (Owner == null) return false;
            Log("====== 玩家点击了重塑选项！开始执行逻辑 ======");

            try
            {
                Log("正在准备弹窗选卡...");
                var prefsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs");
                if (prefsType == null) { Log("报错：找不到 CardSelectorPrefs 类型"); return false; }
                
                var prefs = Activator.CreateInstance(prefsType);
                AccessTools.Property(prefsType, "MinSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "MaxSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "Prompt")?.SetValue(prefs, new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS"));

                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckGeneric");
                if (method == null) { Log("报错：找不到 FromDeckGeneric 方法"); return false; }

                Log("正在调用游戏底层选卡弹窗...");
                var result = method.Invoke(null, new object[] { Owner, prefs }) as System.Collections.IEnumerable;
                if (result == null) { Log("报错：选卡结果返回为空"); return false; }

                List<object> selectedCards = result.Cast<object>().ToList();
                if (selectedCards.Count < 2) { Log("报错：玩家没有选够2张牌"); return false; }

                Log($"成功选中 {selectedCards.Count} 张牌，准备删除...");
                var removeMethod = AccessTools.Method(Owner.Deck.GetType(), "RemoveCard");
                foreach (var card in selectedCards) removeMethod?.Invoke(Owner.Deck, new object[] { card });

                Log("删卡成功！准备发放奖励（此处暂不测试奖励，避免报错）...");
                
                // 为了先跑通点击流程，我们暂时不测试发奖励代码，先让删卡成功生效！
                Log("====== 逻辑执行完成！ ======");
                return true;
            }
            catch (Exception ex)
            {
                Log($"点击时报错了！\n异常: {ex.Message}\n堆栈: {ex.StackTrace}");
                return false;
            }
        }

        private static bool IsValidCard(object card)
        {
            if (card == null) return false;
            Type t = card.GetType();
            string typeStr = AccessTools.Property(t, "Type")?.GetValue(card)?.ToString();
            if (typeStr == "Curse" || typeStr == "Status" || typeStr == "Quest") return false;
            var keywords = AccessTools.Property(t, "Keywords")?.GetValue(card) as System.Collections.IEnumerable;
            if (keywords != null && keywords.Cast<object>().Any(k => k.ToString() == "Eternal")) return false;
            return true;
        }

        private static void Log(string msg)
        {
            try { File.AppendAllText(MyModEntry.LogPath, $"[{DateTime.Now}] {msg}\n"); } catch { }
        }
    }
}