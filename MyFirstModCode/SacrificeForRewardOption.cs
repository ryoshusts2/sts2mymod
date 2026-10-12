using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
        
        // 描述是虚属性，可以安全重写
        public override LocString Description => new LocString("rest_site_ui", "删除两张卡，并视删除卡的价值获得一次卡牌奖励。");

        public override bool IsEnabled
        {
            get
            {
                try
                {
                    if (Owner == null) return false;
                    var deck = AccessTools.Property(Owner.GetType(), "Deck")?.GetValue(Owner);
                    if (deck == null) return false;
                    var cards = AccessTools.Property(deck.GetType(), "Cards")?.GetValue(deck) as IEnumerable;
                    if (cards == null) return false;
                    int count = 0;
                    foreach (var c in cards) { if (IsValidCard(c)) count++; }
                    return count >= 2;
                }
                catch { return false; }
            }
        }

        public override async Task<bool> OnSelect()
        {
            Godot.GD.Print("[MyFirstMod] ======== 玩家点击了重塑选项！开始执行 ========");
            if (Owner == null) return false;

            List<object> selectedCards = null;
            try
            {
                var prefsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs");
                if (prefsType == null) { Godot.GD.Print("[MyFirstMod] 找不到 CardSelectorPrefs"); return false; }

                var prompt = new LocString("rest_site_ui", "选择两张卡牌进行重塑");
                object prefs = null;

                // 【终极修复】优先尝试使用构造函数 (LocString, int, int)
                try
                {
                    prefs = Activator.CreateInstance(prefsType, new object[] { prompt, 2, 2 });
                    Godot.GD.Print("[MyFirstMod] 成功通过构造函数创建 CardSelectorPrefs");
                }
                catch (Exception e)
                {
                    Godot.GD.Print($"[MyFirstMod] 构造函数失败，尝试反射字段: {e.Message}");
                    prefs = Activator.CreateInstance(prefsType);
                    AccessTools.Field(prefsType, "<MinSelect>k__BackingField")?.SetValue(prefs, 2);
                    AccessTools.Field(prefsType, "<MaxSelect>k__BackingField")?.SetValue(prefs, 2);
                    AccessTools.Field(prefsType, "<Prompt>k__BackingField")?.SetValue(prefs, prompt);
                }

                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckGeneric");
                if (method == null) { Godot.GD.Print("[MyFirstMod] 找不到 FromDeckGeneric"); return false; }

                var result = method.Invoke(null, new object[] { Owner, prefs }) as IEnumerable;
                if (result == null) { Godot.GD.Print("[MyFirstMod] 选卡结果为空"); return false; }
                selectedCards = result.Cast<object>().ToList();
            }
            catch (Exception ex) 
            { 
                Godot.GD.Print($"[MyFirstMod] 打开选卡失败: {ex.Message}"); 
                return false; 
            }

            if (selectedCards == null || selectedCards.Count < 2) return false;

            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card)) return false;
            }

            try
            {
                int w1 = GetRarityWeight(selectedCards[0]);
                int w2 = GetRarityWeight(selectedCards[1]);
                int targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);

                var deck = AccessTools.Property(Owner.GetType(), "Deck")?.GetValue(Owner);
                var removeMethod = AccessTools.Method(deck.GetType(), "RemoveCard");
                foreach (var card in selectedCards) removeMethod?.Invoke(deck, new object[] { card });
                
                var cardPool = AccessTools.Property(Owner.Character.GetType(), "CardPool")?.GetValue(Owner.Character);
                var allCardsProp = AccessTools.Property(cardPool.GetType(), "AllCards");
                var allCards = (allCardsProp?.GetValue(cardPool) as IEnumerable)?.Cast<object>().ToList();
                if (allCards == null) return false;

                var rarityType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardRarity");
                object rewardRarity = Enum.Parse(rarityType, targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" });

                var eligible = allCards.Where(c => AccessTools.Property(c.GetType(), "Rarity")?.GetValue(c)?.Equals(rewardRarity) == true).ToList();
                if (!eligible.Any()) eligible = allCards;

                var rewardCard = eligible[new Random().Next(eligible.Count)];

                var optionsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationOptions");
                var options = Activator.CreateInstance(optionsType, new object[] {
                    Array.Empty<object>(),
                    Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"),
                    Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardRarityOddsType"), "Uniform")
                });

                var cardModelType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardModel");
                var listType = typeof(List<>).MakeGenericType(cardModelType);
                var cardList = (IList)Activator.CreateInstance(listType);
                cardList.Add(rewardCard);

                var cardRewardType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.CardReward");
                var rewardCtor = AccessTools.Constructor(cardRewardType, new[] { 
                    typeof(IEnumerable<>).MakeGenericType(cardModelType), 
                    AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), 
                    typeof(Player), 
                    optionsType 
                });
                
                if (rewardCtor == null) return false;
                var reward = rewardCtor.Invoke(new object[] { cardList, Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"), Owner, options });

                var rewardsListType = typeof(List<>).MakeGenericType(AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.Reward"));
                var rewardsList = (IList)Activator.CreateInstance(rewardsListType);
                rewardsList.Add(reward);

                var offerMethod = AccessTools.Method(typeof(RewardsCmd), "OfferCustom");
                if (offerMethod == null) return false;
                
                var task = (Task)offerMethod.Invoke(null, new object[] { Owner, rewardsList });
                await task;
                Godot.GD.Print("[MyFirstMod] ======== 奖励发放成功！========");
                return true;
            }
            catch (Exception ex)
            {
                Godot.GD.Print($"[MyFirstMod] 发奖报错: {ex.Message}");
                return false;
            }
        }

        private static bool IsValidCard(object card)
        {
            if (card == null) return false;
            try
            {
                Type t = card.GetType();
                var isRemovableProp = AccessTools.Property(t, "IsRemovable");
                if (isRemovableProp != null && !(bool)isRemovableProp.GetValue(card)) return false;

                string typeStr = AccessTools.Property(t, "Type")?.GetValue(card)?.ToString();
                if (typeStr == "Curse" || typeStr == "Status" || typeStr == "Quest") return false;

                var keywords = AccessTools.Property(t, "Keywords")?.GetValue(card) as IEnumerable;
                if (keywords != null)
                {
                    foreach (var k in keywords)
                    {
                        string kw = k.ToString();
                        if (kw == "Eternal" || kw == "Event" || kw == "Special" || kw == "Unremovable" || kw == "NoRemove") return false;
                    }
                }

                string rarityStr = AccessTools.Property(t, "Rarity")?.GetValue(card)?.ToString();
                if (rarityStr != "Common" && rarityStr != "Uncommon" && rarityStr != "Rare" && rarityStr != "Ancient") return false;

                return true;
            }
            catch { return false; }
        }

        private static int GetRarityWeight(object card) => AccessTools.Property(card.GetType(), "Rarity")?.GetValue(card)?.ToString() switch { "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Ancient" => 4, _ => 1 };
    }
}