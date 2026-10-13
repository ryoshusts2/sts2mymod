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
        public override LocString Description => new LocString("rest_site_ui", "删除两张卡，并视删除卡的价值获得一次卡牌奖励。");

        private static Type FindTypeByName(string name) => AccessTools.AllTypes().FirstOrDefault(t => t.Name == name);

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
                catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] IsEnabled 报错: {ex.Message}"); return false; }
            }
        }

        public override async Task<bool> OnSelect()
        {
            Godot.GD.Print("[MyFirstMod] ======== 玩家点击了重塑选项！开始执行 ========");
            if (Owner == null) return false;

            List<object> selectedCards = null;
            try
            {
                // 【核心修复1】完全反射创建 CardSelectorPrefs，避免直接引用 internal 类
                var prefsType = FindTypeByName("CardSelectorPrefs");
                if (prefsType == null) { Godot.GD.Print("[MyFirstMod] 找不到 CardSelectorPrefs"); return false; }

                var promptProp = AccessTools.Property(prefsType, "RemoveSelectionPrompt");
                object prompt = promptProp?.GetValue(null) ?? new LocString("rest_site_ui", "选择卡牌删除");

                // 尝试构造实例（允许选1到2张）
                object prefs = null;
                foreach (var args in new object[][] { new object[] { prompt, 1, 2 } })
                {
                    try { prefs = Activator.CreateInstance(prefsType, args); break; } catch { }
                }
                if (prefs == null) return false;

                // 【核心修复2】反射调用 FromDeckForRemoval，这是游戏原生的删卡选牌接口
                var method = AccessTools.Method(typeof(CardSelectCmd), "FromDeckForRemoval");
                if (method == null) { Godot.GD.Print("[MyFirstMod] 找不到 FromDeckForRemoval"); return false; }

                var resultObj = method.Invoke(null, new object[] { Owner, prefs });
                if (resultObj is Task task)
                {
                    await task;
                    var taskResult = task.GetType().GetProperty("Result")?.GetValue(task);
                    if (taskResult is IEnumerable enumerableResult)
                    {
                        selectedCards = enumerableResult.Cast<object>().ToList();
                        Godot.GD.Print($"[MyFirstMod] 选卡成功，玩家选择了 {selectedCards.Count} 张牌");
                    }
                }
            }
            catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打开选卡失败: {ex.Message}"); return false; }

            if (selectedCards == null || selectedCards.Count < 1) return false;
            foreach (var card in selectedCards) { if (!IsValidCard(card)) return false; }

            try
            {
                // 计算稀有度权重（允许选1张或2张）
                int targetQ = 1;
                if (selectedCards.Count == 1)
                {
                    targetQ = GetRarityWeight(selectedCards[0]);
                }
                else
                {
                    int w1 = GetRarityWeight(selectedCards[0]);
                    int w2 = GetRarityWeight(selectedCards[1]);
                    targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);
                }

                // 【核心修复3】动态构造 List<CardModel>，绝对不写死类型
                var cardModelType = FindTypeByName("CardModel");
                var listType = typeof(List<>).MakeGenericType(cardModelType);
                var selectedCardList = (IList)Activator.CreateInstance(listType);
                foreach (var card in selectedCards) { selectedCardList.Add(card); }

                // 【核心修复4】使用游戏原生的 CardPileCmd.RemoveFromDeck 彻底删除卡牌（自带同步，防止回滚）
                var removeMethod = AccessTools.Method(typeof(CardPileCmd), "RemoveFromDeck");
                if (removeMethod == null) { Godot.GD.Print("[MyFirstMod] 找不到 RemoveFromDeck 方法"); return false; }
                
                var removeTask = removeMethod.Invoke(null, new object[] { selectedCardList });
                if (removeTask is Task taskToWait) await taskToWait;
                
                Godot.GD.Print($"[MyFirstMod] 成功调用 RemoveFromDeck 删除了 {selectedCardList.Count} 张卡（已同步状态，绝不回滚）");

                // --- 以下是发奖励逻辑（保持之前能成功弹窗的版本） ---
                var cardPool = AccessTools.Property(Owner.Character.GetType(), "CardPool")?.GetValue(Owner.Character);
                var poolType = FindTypeByName("CardPoolModel");
                var poolsList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(poolType));
                poolsList.Add(cardPool);

                var rarityType = FindTypeByName("CardRarity");
                object rewardRarity = Enum.Parse(rarityType, targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" });

                var optionsType = FindTypeByName("CardCreationOptions");
                var sourceType = FindTypeByName("CardCreationSource");
                var oddsType = FindTypeByName("CardRarityOddsType");
                var cardRewardType = FindTypeByName("CardReward");

                // 创建过滤器委托，确保奖励池里没有基础牌和诅咒牌
                var filterMethod = typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic);
                var filterDelegate = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool)), filterMethod);

                object options = Activator.CreateInstance(optionsType, new object[] {
                    poolsList,
                    Enum.Parse(sourceType, "Other"),
                    Enum.Parse(oddsType, "Uniform"),
                    filterDelegate
                });

                object reward = null;
                foreach (var ctor in cardRewardType.GetConstructors())
                {
                    try
                    {
                        var ps = ctor.GetParameters();
                        var args = new List<object>();
                        foreach (var p in ps)
                        {
                            if (p.ParameterType == optionsType) args.Add(options);
                            else if (p.ParameterType == typeof(int)) args.Add(1);
                            else if (p.ParameterType == typeof(Player)) args.Add(Owner);
                            else if (p.ParameterType.IsValueType) args.Add(Activator.CreateInstance(p.ParameterType));
                            else args.Add(null);
                        }
                        reward = ctor.Invoke(args.ToArray());
                        if (reward != null) break;
                    }
                    catch { }
                }

                if (reward == null) { Godot.GD.Print("[MyFirstMod] 找不到匹配的 CardReward 构造函数"); return false; }

                var rewardsListType = typeof(List<>).MakeGenericType(FindTypeByName("Reward"));
                var rewardsList = (IList)Activator.CreateInstance(rewardsListType);
                rewardsList.Add(reward);

                var offerMethod = AccessTools.Method(typeof(RewardsCmd), "OfferCustom");
                if (offerMethod == null) return false;

                var offerTask = (Task)offerMethod.Invoke(null, new object[] { Owner, rewardsList });
                await offerTask;
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
                if (rarityStr != "Common" && rarityStr != "Uncommon" && rarityStr != "Rare" && rarityStr != "Ancient" && rarityStr != "Basic") return false;

                return true;
            }
            catch { return false; }
        }

        private static int GetRarityWeight(object card) => AccessTools.Property(card.GetType(), "Rarity")?.GetValue(card)?.ToString() switch { "Basic" => 1, "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Ancient" => 4, _ => 1 };
    }
}