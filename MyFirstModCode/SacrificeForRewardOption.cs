using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Models;

namespace MyFirstModCode
{
    public class SacrificeForRewardOption : RestSiteOption
    {
        public SacrificeForRewardOption(Player owner) : base(owner) { }

        public override string OptionId => "MYFIRSTMOD_SACRIFICE_FOR_REWARD";

        // 如果不管怎么试都显示 dig，我们可以尝试直接返回 null 或空，或者接受它。这里保留原样。
        public override LocString Description => new LocString("rest_site_ui", "MYFIRSTMOD_RESHUFFLE_DESC");

        public override bool IsEnabled
        {
            get
            {
                if (Owner == null || Owner.Deck == null) return false;
                int validCount = Owner.Deck.Cards.Count(IsValidCard);
                return validCount >= 2;
            }
        }

        public override async Task<bool> OnSelect()
        {
            if (Owner == null) return false;

            List<CardModel> selectedCards = null;
            try 
            {
                // 修改：去除 Filter 属性，靠 UI 选择后在代码里拦截
                var result = await CardSelectCmd.FromDeckGeneric(
                    Owner,
                    new CardSelectorPrefs
                    {
                        MinSelect = 2,
                        MaxSelect = 2,
                        Prompt = new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS")
                    });
                selectedCards = result?.ToList();
            }
            catch (Exception ex)
            {
                LogError($"打开选卡界面失败", ex);
                return false;
            }

            if (selectedCards == null || selectedCards.Count < 2) return false;

            // 严格的二次拦截（防止玩家钻空子）
            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card))
                {
                    LogError($"玩家尝试选择了违规卡牌: {card.Name}，操作已取消。");
                    return false; // 发现有违规，直接取消
                }
            }

            try 
            {
                int weight1 = GetRarityWeight(selectedCards[0].Rarity);
                int weight2 = GetRarityWeight(selectedCards[1].Rarity);
                int targetQuality = Math.Clamp((weight1 + weight2) / 2, 1, 4);

                // 删除卡牌
                foreach (var card in selectedCards)
                {
                    Owner.Deck.RemoveCard(card);
                }

                CardRarity rewardRarity = QualityToRarity(targetQuality);
                LogError($"成功移除卡牌，计算出的奖励品质为: {rewardRarity}，正在尝试发放奖励...");

                try 
                {
                    var reward = new CardReward(rewardRarity);
                    await RewardsCmd.OfferCustom(Owner, reward);
                    LogError($"奖励发放成功！");
                }
                catch (Exception rewardEx)
                {
                    LogError($"发放奖励失败！", rewardEx);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError("删除卡牌或计算奖励时出错", ex);
                return false;
            }
        }

        private static bool IsValidCard(CardModel card)
        {
            if (card == null || card.IsLocked) return false;

            // 检查永恒，用 try-catch 防止属性不存在崩溃
            try
            {
                if (card.IsEternal) return false; 
            }
            catch { /* 属性不存在，忽略 */ }

            // 绝对排除：诅咒、状态、任务、事件牌
            if (card.Type == CardType.Curse || 
                card.Type == CardType.Status || 
                card.Type == CardType.Quest || 
                card.Type == CardType.Event) 
                return false;

            // 只保留有明确品质的牌（普通、罕见、稀有、先古）
            return card.Rarity == CardRarity.Common 
                || card.Rarity == CardRarity.Uncommon 
                || card.Rarity == CardRarity.Rare 
                || card.Rarity == CardRarity.Ancient;
        }

        private static int GetRarityWeight(CardRarity rarity) => rarity switch
        {
            CardRarity.Common   => 1,
            CardRarity.Uncommon => 2,
            CardRarity.Rare     => 3,
            CardRarity.Ancient  => 4,
            _                   => 1
        };

        private static CardRarity QualityToRarity(int quality) => quality switch
        {
            1 => CardRarity.Common,
            2 => CardRarity.Uncommon,
            3 => CardRarity.Rare,
            4 => CardRarity.Ancient,
            _ => CardRarity.Common
        };

        // 日志辅助方法，确保错误不被吞掉
        private static void LogError(string msg, Exception ex = null)
        {
            try {
                string errorMsg = $"[{DateTime.Now}] {msg}";
                if (ex != null) errorMsg += $"\n异常: {ex.Message}\n堆栈: {ex.StackTrace}";
                File.AppendAllText(MyModEntry.LogPath, errorMsg + "\n");
            } catch { }
        }
    }
}
