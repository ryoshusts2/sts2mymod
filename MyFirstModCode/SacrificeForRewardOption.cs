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
        
        // 硬编码介绍文本，绕过缺失的翻译文件
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
                // 1. 准备选卡参数
                var prefsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs");
                if (prefsType == null) { Godot.GD.Print("[MyFirstMod] 错误: 找不到 CardSelectorPrefs 类型"); return false; }
                
                var prefs = Activator.CreateInstance(prefsType);
                AccessTools.Property(prefsType, "MinSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "MaxSelect")?.SetValue(prefs, 2);
                AccessTools.Property(prefsType, "Prompt")?.SetValue(prefs, new LocString("rest_site_ui", "选择两张卡牌进行重塑"));

                // 2. 尝试将过滤器注入到选卡参数中，使UI直接隐藏不能删的牌
                var filterProp = AccessTools.Property(prefsType, "Filter") ?? AccessTools.Property(prefsType, "CardFilter");
                if (filterProp != null)
                {
                    var cardModelType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardModel");
                    var funcType = typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool));
                    var parameter = Expression.Parameter(cardModelType, "card");
                    var methodCall = Expression.Call(typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic), Expression.Convert(parameter, typeof(object)));
                    var lambda = Expression.Lambda(funcType, methodCall, parameter).Compile();
                    filterProp.SetValue(prefs, lambda);
                    Godot.GD.Print("[MyFirstMod] 成功将过滤器注入选卡界面，不能删的牌将不会显示。");
                }
                else
                {
                    Godot.GD.Print("[MyFirstMod] 警告: 未找到 Filter 属性，将依赖点击后的二次拦截。");
                }

                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckGeneric");
                if (method == null) { Godot.GD.Print("[MyFirstMod] 错误: 找不到 FromDeckGeneric 方法"); return false; }

                var result = method.Invoke(null, new object[] { Owner, prefs }) as IEnumerable;
                if (result == null) { Godot.GD.Print("[MyFirstMod] 错误: 选卡结果返回为空"); return false; }
                selectedCards = result.Cast<object>().ToList();
            }
            catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打开选卡界面失败: {ex.Message}\n{ex.StackTrace}"); return false; }

            if (selectedCards == null || selectedCards.Count < 2) return false;

            // 3. 二次强校验，严防违规牌钻空子
            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card)) 
                {
                    Godot.GD.Print($"[MyFirstMod] 拦截到违规卡牌，操作取消！");
                    return false; 
                }
            }

            try
            {
                int w1 = GetRarityWeight(selectedCards[0]);
                int w2 = GetRarityWeight(selectedCards[1]);
                int targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);
                Godot.GD.Print($"[MyFirstMod] 两张牌稀有度计算完毕，目标奖励品质等级为: {targetQ}");

                // 4. 执行删牌
                var deck = AccessTools.Property(Owner.GetType(), "Deck")?.GetValue(Owner);
                var removeMethod = AccessTools.Method(deck.GetType(), "RemoveCard");
                foreach (var card in selectedCards) removeMethod?.Invoke(deck, new object[] { card });
                Godot.GD.Print($"[MyFirstMod] 成功删除 {selectedCards.Count} 张牌！准备发奖励...");

                // 5. 根据稀有度抽取奖励卡牌
                var cardPool = AccessTools.Property(Owner.Character.GetType(), "CardPool")?.GetValue(Owner.Character);
                var allCardsProp = AccessTools.Property(cardPool.GetType(), "AllCards");
                var allCards = (allCardsProp?.GetValue(cardPool) as IEnumerable)?.Cast<object>().ToList();
                if (allCards == null) return false;

                var rarityType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardRarity");
                object rewardRarity = Enum.Parse(rarityType, targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" });

                var eligible = allCards.Where(c => AccessTools.Property(c.GetType(), "Rarity")?.GetValue(c)?.Equals(rewardRarity) == true).ToList();
                if (!eligible.Any()) eligible = allCards;

                var rewardCard = eligible[new Random().Next(eligible.Count)];

                // 6. 构造并发放奖励
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
                
                if (rewardCtor == null) { Godot.GD.Print("[MyFirstMod] 错误: 找不到 CardReward 构造函数"); return false; }
                var reward = rewardCtor.Invoke(new object[] { cardList, Enum.Parse(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardCreationSource"), "Other"), Owner, options });

                var rewardsListType = typeof(List<>).MakeGenericType(AccessTools.TypeByName("MegaCrit.Sts2.Core.Rewards.Reward"));
                var rewardsList = (IList)Activator.CreateInstance(rewardsListType);
                rewardsList.Add(reward);

                var offerMethod = AccessTools.Method(typeof(RewardsCmd), "OfferCustom");
                if (offerMethod == null) { Godot.GD.Print("[MyFirstMod] 错误: 找不到 OfferCustom 方法"); return false; }
                
                var task = (Task)offerMethod.Invoke(null, new object[] { Owner, rewardsList });
                await task;
                Godot.GD.Print("[MyFirstMod] ======== 奖励发放成功！Mod 逻辑完美跑通！ ========");
                return true;
            }
            catch (Exception ex)
            {
                Godot.GD.Print($"[MyFirstMod] 逻辑执行致命报错: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        // 【绝对严格】判断一张牌能否被本Mod删除。
        // 规则：排除诅咒、状态、任务、永恒牌、事件牌（绿框）、以及任何没有明确品质的牌。
        private static bool IsValidCard(object card)
        {
            if (card == null) return false;
            try
            {
                Type t = card.GetType();
                
                // 1. 排除不可移除的牌（例如永恒牌）
                var isRemovableProp = AccessTools.Property(t, "IsRemovable");
                if (isRemovableProp != null && !(bool)isRemovableProp.GetValue(card)) return false;

                // 2. 绝对排除：诅咒、状态、任务
                string typeStr = AccessTools.Property(t, "Type")?.GetValue(card)?.ToString();
                if (typeStr == "Curse" || typeStr == "Status" || typeStr == "Quest") return false;

                // 3. 绝对排除：事件牌、特殊牌（包含 Event、Special、Unremovable 等关键词）
                var keywords = AccessTools.Property(t, "Keywords")?.GetValue(card) as IEnumerable;
                if (keywords != null)
                {
                    foreach (var k in keywords)
                    {
                        string kw = k.ToString();
                        if (kw == "Eternal" || kw == "Event" || kw == "Special" || kw == "Unremovable" || kw == "NoRemove") 
                            return false;
                    }
                }

                // 4. 只保留有明确品质的牌（普通、罕见、稀有、先古）
                string rarityStr = AccessTools.Property(t, "Rarity")?.GetValue(card)?.ToString();
                if (rarityStr != "Common" && rarityStr != "Uncommon" && rarityStr != "Rare" && rarityStr != "Ancient")
                    return false;

                return true;
            }
            catch { return false; }
        }

        private static int GetRarityWeight(object card) => AccessTools.Property(card.GetType(), "Rarity")?.GetValue(card)?.ToString() switch { "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Ancient" => 4, _ => 1 };
    }
}