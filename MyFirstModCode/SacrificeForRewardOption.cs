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

        // 因为我们不提供翻译文件，所以这里显示的是默认的占位符 "dig"。
        // 但这不影响逻辑功能！我们优先确保功能跑通。
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
                var result = await CardSelectCmd.FromDeckGeneric(
                    Owner,
                    new CardSelectorPrefs
                    {
                        MinSelect = 2,
                        MaxSelect = 2,
                        Prompt = new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS"),
                        // 注意：API 中可能没有 Filter 属性，我们不依赖它，完全靠代码二次拦截
                    });
                selectedCards = result?.ToList();
            }
            catch (Exception ex)
            {
                LogError($"打开选卡界面失败", ex);
                return false;
            }

            if (selectedCards == null || selectedCards.Count < 2) return false;

            // 【严格二次拦截】防止玩家钻空子
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

                // 1. 执行删除
                foreach (var card in selectedCards)
                {
                    Owner.Deck.RemoveCard(card);
                }

                CardRarity rewardRarity = QualityToRarity(targetQuality);
                LogError($"成功移除卡牌，计算出的奖励品质为: {rewardRarity}，正在尝试发放奖励...");

                // 2. 发放奖励（使用真实的 API）
                try 
                {
                    // 从玩家的角色卡池中，按稀有度筛选符合条件的卡牌
                    List<CardModel> eligibleCards = Owner.Character.CardPool.AllCards
                        .Where(c => c.Rarity == rewardRarity)
                        .ToList();

                    if (eligibleCards.Count == 0)
                    {
                        // 如果该稀有度没牌，退化为所有卡
                        eligibleCards = Owner.Character.CardPool.AllCards.ToList();
                    }

                    // 随机抽取一张
                    var random = new Random();
                    var rewardCard = eligibleCards[random.Next(eligibleCards.Count)];

                    // 构建真实源码里的 CardCreationOptions
                    var options = new CardCreationOptions(
                        Array.Empty<CardPoolModel>(), // 我们不提供卡池，直接提供具体卡牌
                        CardCreationSource.Other,      // 来源设为 Other
                        CardRarityOddsType.Uniform     // 稀有度权重设为统一（因为我们只给一张）
                    );

                    // 使用真实的构造函数：CardReward(IEnumerable<CardModel> cardsToOffer, CardCreationSource source, Player player, CardCreationOptions rerollOptions, ...)
                    var reward = new CardReward(
                        new List<CardModel> { rewardCard }, 
                        CardCreationSource.Other, 
                        Owner, 
                        options
                    );

                    // 用真实的发奖方法：OfferCustom
                    await RewardsCmd.OfferCustom(Owner, new List<Reward> { reward });
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

        /// <summary>
        /// 【绝对严格】判断一张牌能否被本Mod删除。
        /// 规则：只有有品质的卡才能删。
        /// 绝对排除：诅咒、状态、任务、永恒牌。
        /// </summary>
        private static bool IsValidCard(CardModel card)
        {
            if (card == null) return false;

            // 1. 绝对排除：诅咒、状态、任务（从真实源码确认了枚举名）
            if (card.Type == CardType.Curse || 
                card.Type == CardType.Status || 
                card.Type == CardType.Quest) 
                return false;

            // 2. 绝对排除：永恒牌（从真实源码确认了关键词写法）
            if (card.Keywords.Contains(CardKeyword.Eternal)) 
                return false;

            // 3. 只保留有明确品质的牌
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