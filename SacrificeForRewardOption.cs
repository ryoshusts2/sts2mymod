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
        public override LocString Description => new LocString("rest_site_ui", "MYFIRSTMOD_RESHUFFLE_DESC");

        public override bool IsEnabled
        {
            get
            {
                if (Owner == null || Owner.Deck == null) return false;
                return Owner.Deck.Cards.Count(IsValidCard) >= 2;
            }
        }

        public override async Task<bool> OnSelect()
        {
            if (Owner == null) return false;

            List<CardModel> selectedCards = null;
            try 
            {
                var result = await CardSelectCmd.FromDeckGeneric(Owner, new CardSelectorPrefs { MinSelect = 2, MaxSelect = 2, Prompt = new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS") });
                selectedCards = result?.ToList();
            }
            catch (Exception ex) { LogError($"打开选卡界面失败", ex); return false; }

            if (selectedCards == null || selectedCards.Count < 2) return false;

            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card)) { LogError($"玩家尝试选择了违规卡牌: {card.Name}，操作已取消。"); return false; }
            }

            try 
            {
                int weight1 = GetRarityWeight(selectedCards[0].Rarity);
                int weight2 = GetRarityWeight(selectedCards[1].Rarity);
                int targetQuality = Math.Clamp((weight1 + weight2) / 2, 1, 4);

                foreach (var card in selectedCards) Owner.Deck.RemoveCard(card);

                CardRarity rewardRarity = QualityToRarity(targetQuality);
                LogError($"成功移除卡牌，奖励品质: {rewardRarity}，发放奖励...");

                try 
                {
                    List<CardModel> eligibleCards = Owner.Character.CardPool.AllCards.Where(c => c.Rarity == rewardRarity).ToList();
                    if (eligibleCards.Count == 0) eligibleCards = Owner.Character.CardPool.AllCards.ToList();

                    var random = new Random();
                    var rewardCard = eligibleCards[random.Next(eligibleCards.Count)];

                    var options = new CardCreationOptions(Array.Empty<CardPoolModel>(), CardCreationSource.Other, CardRarityOddsType.Uniform);
                    var reward = new CardReward(new List<CardModel> { rewardCard }, CardCreationSource.Other, Owner, options);
                    await RewardsCmd.OfferCustom(Owner, new List<Reward> { reward });
                    LogError($"奖励发放成功！");
                }
                catch (Exception rewardEx) { LogError($"发放奖励失败！", rewardEx); }
                return true;
            }
            catch (Exception ex) { LogError("删除卡牌或计算奖励时出错", ex); return false; }
        }

        private static bool IsValidCard(CardModel card)
        {
            if (card == null) return false;
            if (card.Type == CardType.Curse || card.Type == CardType.Status || card.Type == CardType.Quest) return false;
            if (card.Keywords.Contains(CardKeyword.Eternal)) return false;
            return card.Rarity == CardRarity.Common || card.Rarity == CardRarity.Uncommon || card.Rarity == CardRarity.Rare || card.Rarity == CardRarity.Ancient;
        }

        private static int GetRarityWeight(CardRarity rarity) => rarity switch { CardRarity.Common => 1, CardRarity.Uncommon => 2, CardRarity.Rare => 3, CardRarity.Ancient => 4, _ => 1 };
        private static CardRarity QualityToRarity(int quality) => quality switch { 1 => CardRarity.Common, 2 => CardRarity.Uncommon, 3 => CardRarity.Rare, 4 => CardRarity.Ancient, _ => CardRarity.Common };

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