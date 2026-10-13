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
                var prefsType = FindTypeByName("CardSelectorPrefs");
                if (prefsType == null) { Godot.GD.Print("[MyFirstMod] 找不到 CardSelectorPrefs"); return false; }

                var promptProp = AccessTools.Property(prefsType, "RemoveSelectionPrompt");
                object prompt = promptProp?.GetValue(null) ?? new LocString("rest_site_ui", "选择卡牌删除");

                // 【修复Bug 1：允许返回】将 MinSelect 从 1 改为 0，让玩家可以点返回按钮
                object prefs = null;
                foreach (var args in new object[][] { new object[] { prompt, 0, 2 } })
                {
                    try { prefs = Activator.CreateInstance(prefsType, args); break; } catch { }
                }
                if (prefs == null) return false;

                // 【修复Bug 2：UI层过滤任务牌】注入Filter，让不可删的牌直接在UI消失
                try
                {
                    var filterProp = AccessTools.Property(prefsType, "Filter") ?? AccessTools.Property(prefsType, "CardFilter");
                    var filterField = AccessTools.Field(prefsType, "<Filter>k__BackingField") ?? AccessTools.Field(prefsType, "<CardFilter>k__BackingField");

                    var cardModelType = FindTypeByName("CardModel");
                    var funcType = typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool));
                    var parameter = Expression.Parameter(cardModelType, "card");
                    var methodCall = Expression.Call(typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic), Expression.Convert(parameter, typeof(object)));
                    var filterDelegate = Expression.Lambda(funcType, methodCall, parameter).Compile();

                    if (filterProp != null && filterProp.CanWrite)
                    {
                        filterProp.SetValue(prefs, filterDelegate);
                        Godot.GD.Print("[MyFirstMod] 成功注入 Filter 属性，任务牌/诅咒牌将不再显示在UI中");
                    }
                    else if (filterField != null)
                    {
                        filterField.SetValue(prefs, filterDelegate);
                        Godot.GD.Print("[MyFirstMod] 成功注入 Filter 字段，任务牌/诅咒牌将不再显示在UI中");
                    }
                    else
                    {
                        Godot.GD.Print("[MyFirstMod] 警告: 无法注入 Filter，UI仍会显示任务牌，但二次拦截会阻止删除。");
                    }
                }
                catch (Exception ex)
                {
                    Godot.GD.Print($"[MyFirstMod] 注入Filter时出错（不影响二次拦截）: {ex.Message}");
                }

                var methods = AccessTools.GetDeclaredMethods(typeof(CardSelectCmd))
                    .Where(m => m.Name == "FromDeckForRemoval")
                    .OrderBy(m => m.GetParameters().Length)
                    .ToList();

                foreach (var method in methods)
                {
                    var ps = method.GetParameters();
                    var args = new List<object> { Owner, prefs };

                    for (int i = 2; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType.IsValueType) args.Add(Activator.CreateInstance(ps[i].ParameterType));
                        else args.Add(null);
                    }

                    try
                    {
                        var resultObj = method.Invoke(null, args.ToArray());
                        if (resultObj is Task task)
                        {
                            await task;
                            var taskResult = task.GetType().GetProperty("Result")?.GetValue(task);
                            if (taskResult is IEnumerable enumerableResult)
                            {
                                selectedCards = enumerableResult.Cast<object>().ToList();
                                Godot.GD.Print($"[MyFirstMod] 选卡完成，拿到 {selectedCards.Count} 张牌");
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打开选卡失败: {ex.Message}"); return false; }

            // 【修复Bug 1：返回处理】如果玩家点了返回（选了0张），直接安全退出，不删卡不发奖
            if (selectedCards == null || selectedCards.Count < 1)
            {
                Godot.GD.Print("[MyFirstMod] 玩家取消了操作，安全返回篝火。");
                return false;
            }

            // 二次拦截兜底
            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card)) 
                {
                    Godot.GD.Print($"[MyFirstMod] 玩家选中了违规卡牌，操作取消。");
                    return false;
                }
            }

            try
            {
                int targetQ = 1;
                if (selectedCards.Count == 1) { targetQ = GetRarityWeight(selectedCards[0]); }
                else { int w1 = GetRarityWeight(selectedCards[0]); int w2 = GetRarityWeight(selectedCards[1]); targetQ = (int)Math.Round((w1 + w2) / 2.0); }
                if (targetQ < 1) targetQ = 1;
                if (targetQ > 4) targetQ = 4;
                string targetRarityStr = targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" };
                Godot.GD.Print($"[MyFirstMod] 目标品质等级为 {targetQ}（{targetRarityStr}）");

                var removeMethods = AccessTools.GetDeclaredMethods(typeof(CardPileCmd)).Where(m => m.Name == "RemoveFromDeck").ToList();
                MethodInfo singleCardMethod = null;
                foreach (var m in removeMethods)
                {
                    var ps = m.GetParameters();
                    if (ps.Length >= 1 && ps.Length <= 2 && ps[0].ParameterType.Name == "CardModel") { singleCardMethod = m; break; }
                }
                if (singleCardMethod == null) { Godot.GD.Print("[MyFirstMod] 找不到 RemoveFromDeck 重载"); return false; }

                foreach (var card in selectedCards)
                {
                    var removeTask = singleCardMethod.Invoke(null, new object[] { card, true });
                    if (removeTask is Task taskToWait) await taskToWait;
                }
                Godot.GD.Print($"[MyFirstMod] 成功删除了 {selectedCards.Count} 张卡");

                // 发奖励
                var cardModelType = FindTypeByName("CardModel");
                var cardPool = AccessTools.Property(Owner.Character.GetType(), "CardPool")?.GetValue(Owner.Character);
                var poolType = FindTypeByName("CardPoolModel");
                var poolsList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(poolType));
                poolsList.Add(cardPool);

                var rarityType = FindTypeByName("CardRarity");
                object rewardRarity = Enum.Parse(rarityType, targetRarityStr);

                var optionsType = FindTypeByName("CardCreationOptions");
                var sourceType = FindTypeByName("CardCreationSource");
                var oddsType = FindTypeByName("CardRarityOddsType");
                var cardRewardType = FindTypeByName("CardReward");

                var parameter = Expression.Parameter(cardModelType, "card");
                var isValidCall = Expression.Call(typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic), Expression.Convert(parameter, typeof(object)));
                var rarityProp = Expression.Property(parameter, "Rarity");
                var targetRarityConst = Expression.Constant(rewardRarity, rarityProp.Type);
                var rarityCheck = Expression.Equal(rarityProp, targetRarityConst);
                var body = Expression.AndAlso(isValidCall, rarityCheck);
                var filterDelegate = Expression.Lambda(typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool)), body, parameter).Compile();

                object options = Activator.CreateInstance(optionsType, new object[] {
                    poolsList, Enum.Parse(sourceType, "Other"), Enum.Parse(oddsType, "Uniform"), filterDelegate
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

        private static int GetRarityWeight(object card) => AccessTools.Property(card.GetType(), "Rarity")?.GetValue(card)?.ToString() switch 
        { 
            "Basic" => 1, "Common" => 1, "Uncommon" => 2, "Rare" => 3, "Ancient" => 4, _ => 1 
        };
    }
}