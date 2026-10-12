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
            get { return Owner?.Deck != null && Owner.Deck.Cards.Count(IsValidCard) >= 2; }
        }

        public override async Task<bool> OnSelect()
        {
            if (Owner == null) return false;

            List<object> selectedCards = null;
            try
            {
                var prefsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs");
                var prefs = Activator.CreateInstance(prefsType);
                AccessTools.Property(prefsType, "MinSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "MaxSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "Prompt")?.SetValue(prefs, new LocString("rest_site_ui", "MYFIRSTMOD_SELECT_TWO_CARDS"));

                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckGeneric");
                var result = method.Invoke(null, new object[] { Owner, prefs }) as IEnumerable<object>;
                selectedCards = result?.ToList();
            }
            catch (Exception ex) { LogError("打开选卡界面失败", ex); return false; }

            if (selectedCards == null || selectedCards.Count < 2) return false;
            foreach (var card in selectedCards) { if (!IsValidCard(card)) return false; }

            try
            {
                int w1 = GetRarityWeight(selectedCards[0]);
                int w2 = GetRarityWeight(selectedCards[1]);
                int targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);

                var removeMethod = AccessTools.Method(Owner.Deck.GetType(), "RemoveCard");
                foreach (var card in selectedCards) removeMethod?.Invoke(Owner.Deck, new object[] { card });

                var rarityType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardRarity");
                object rewardRarity = QualityToRarity(targetQ, rarityType);

                var allCardsProp = AccessTools.Property(Owner.Character.CardPool.GetType(), "AllCards");
                var allCards = (allCardsProp.GetValue(Owner.Character.CardPool) as IEnumerable<object>)?.ToList();
                if (allCards == null) return false;

                var eligible = allCards.Where(c => AccessTools.Property(c.GetType(), "Rarity")?.GetValue(c)?.Equals(rewardRarity) == true).ToList();
                if (!eligible.Any()) eligible = allCards;

                object rewardCard = eligible[new Random().Next(eligible.Count)];

                var optionsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationOptions");
                var options = Activator.CreateInstance(optionsType, new object[] {
                    Array.Empty<object>(),
                    Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"),
                    Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardRarityOddsType"), "Uniform")
                });

                // 用反射获取 CardReward 类，因为它是 internal 的
                var cardRewardType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.CardReward");
                var rewardCtor = AccessTools.Constructor(cardRewardType, new[] { typeof(IEnumerable<object>), AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), typeof(Player), optionsType });
                var reward = rewardCtor.Invoke(new object[] { new List<object> { rewardCard }, Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"), Owner, options });

                await RewardsCmd.OfferCustom(Owner, new List<Reward> { reward as Reward });
                return true;
            }
            catch (Exception ex) { LogError("发放奖励失败", ex); return false; }
        }

        private static bool IsValidCard(object card)
        {
            if (card == null) return false;
            Type t = card.GetType();
            string typeStr = AccessTools.Property(t, "Type")?.GetValue(card)?.ToString();
            if (typeStr == "Curse" || typeStr == "Status" || typeStr == "Quest") return false;
            var keywords = AccessTools.Property(t, "Keywords")?.GetValue(card) as IEnumerable<object>;
            if (keywords != null && keywords.Any(k => k.ToString() == "Eternal")) return false;
            return true;
        }

        private static int GetRarityWeight(object card) => AccessTools.Property(card.GetType(), "Rarity")?.GetValue(card)?.ToString() switch { "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Ancient" => 4, _ => 1 };
        private static object QualityToRarity(int q, Type t) => Enum.Parse(t, q switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" });

        private static void LogError(string msg, Exception ex = null)
        {
            try { File.AppendAllText(MyModEntry.LogPath, $"[{DateTime.Now}] {msg}: {ex?.Message}\n"); } catch { }
        }
    }
}