using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Rewards;

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
                if (Owner?.Deck == null) return false;
                return Owner.Deck.Cards.Count(IsValidCard) >= 2;
            }
        }

        public override async Task<bool> OnSelect()
        {
            if (Owner == null) return false;

            List<CardModel> selectedCards = null;
            try
            {
                var prefsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs");
                var prefs = Activator.CreateInstance(prefsType);
                AccessTools.Property(prefsType, "MinSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "MaxSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "Prompt")?.SetValue(prefs, new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS"));

                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckGeneric");
                var result = method.Invoke(null, new object[] { Owner, prefs }) as IEnumerable<CardModel>;
                selectedCards = result?.ToList();
            }
            catch (Exception ex) { LogError("打开选卡界面失败", ex); return false; }

            if (selectedCards == null || selectedCards.Count < 2) return false;
            foreach (var card in selectedCards) { if (!IsValidCard(card)) return false; }

            try
            {
                int w1 = GetRarityWeight(selectedCards[0].Rarity);
                int w2 = GetRarityWeight(selectedCards[1].Rarity);
                int targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);

                foreach (var card in selectedCards)
                {
                    AccessTools.Method(Owner.Deck.GetType(), "RemoveCard")?.Invoke(Owner.Deck, new object[] { card });
                }

                CardRarity rewardRarity = QualityToRarity(targetQ);
                var eligibleCards = Owner.Character.CardPool.AllCards.Where(c => c.Rarity == rewardRarity).ToList();
                if (eligibleCards.Count == 0) eligibleCards = Owner.Character.CardPool.AllCards.ToList();

                var rewardCard = eligibleCards[new Random().Next(eligibleCards.Count)];

                var optionsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationOptions");
                var options = Activator.CreateInstance(optionsType, new object[] {
                    Array.Empty<object>(), Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"), Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardRarityOddsType"), "Uniform")
                });

                var rewardType = typeof(CardReward);
                var ctor = AccessTools.Constructor(rewardType, new[] { typeof(IEnumerable<CardModel>), AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), typeof(Player), optionsType });
                var reward = ctor.Invoke(new object[] { new List<CardModel> { rewardCard }, Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"), Owner, options });

                await RewardsCmd.OfferCustom(Owner, new List<Reward> { reward as Reward });
                return true;
            }
            catch (Exception ex) { LogError("发放奖励失败", ex); return false; }
        }

        private static bool IsValidCard(CardModel card)
        {
            if (card == null) return false;
            if (card.Type == CardType.Curse || card.Type == CardType.Status || card.Type == CardType.Quest) return false;
            if (card.Keywords.Contains(CardKeyword.Eternal)) return false;
            return card.Rarity == CardRarity.Common || card.Rarity == CardRarity.Uncommon || card.Rarity == CardRarity.Rare || card.Rarity == CardRarity.Ancient;
        }

        private static int GetRarityWeight(CardRarity r) => r switch { CardRarity.Common => 1, CardRarity.Uncommon => 2, CardRarity.Rare => 3, CardRarity.Ancient => 4, _ => 1 };
        private static CardRarity QualityToRarity(int q) => q switch { 1 => CardRarity.Common, 2 => CardRarity.Uncommon, 3 => CardRarity.Rare, 4 => CardRarity.Ancient, _ => CardRarity.Common };

        private static void LogError(string msg, Exception ex = null)
        {
            try { File.AppendAllText(MyModEntry.LogPath, $"[{DateTime.Now}] {msg}: {ex?.Message}\n"); } catch { }
        }
    }
}