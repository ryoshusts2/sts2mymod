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

                // 硬编码中文提示，避免本地化缺失引起的显示问题
                var prompt = new LocString("rest_site_ui", "选择两张卡牌进行重塑");
                object prefs = null;
                foreach (var args in new object[][] { new object[] { prompt, 2, 2 } })
                {
                    try { prefs = Activator.CreateInstance(prefsType, args); break; } catch { }
                }
                if (prefs == null) return false;

                var methods = AccessTools.GetDeclaredMethods(typeof(CardSelectCmd)).Where(m => m.Name == "FromDeckGeneric").OrderBy(m => m.GetParameters().Length).ToList();
                foreach (var method in methods)
                {
                    var args = new List<object> { Owner, prefs };
                    var ps = method.GetParameters();
                    for (int i = 2; i < ps.Length; i++) args.Add(ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);

                    try
                    {
                        var resultObj = method.Invoke(null, args.ToArray());
                        if (resultObj is Task task)
                        {
                            await task;
                            var taskResult = task.GetType().GetProperty("Result")?.GetValue(task);
                            if (taskResult is IEnumerable enumerableResult) { selectedCards = enumerableResult.Cast<object>().ToList(); break; }
                        }
                    }
                    catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 调用 FromDeckGeneric 失败: {ex.InnerException?.Message ?? ex.Message}"); }
                }
            }
            catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打开选卡失败: {ex.Message}"); return false; }

            // 严格要求选2张卡
            if (selectedCards == null || selectedCards.Count < 2) return false;

            foreach (var card in selectedCards) { if (!IsValidCard(card)) return false; }

            try
            {
                int w1 = GetRarityWeight(selectedCards[0]);
                int w2 = GetRarityWeight(selectedCards[1]);
                int targetQ = Math.Clamp((w1 + w2) / 2, 1, 4);

                var deck = AccessTools.Property(Owner.GetType(), "Deck")?.GetValue(Owner);
                var removeMethod = AccessTools.Method(deck.GetType(), "RemoveCard");
                foreach (var card in selectedCards) removeMethod?.Invoke(deck, new object[] { card });
                Godot.GD.Print($"[MyFirstMod] 成功删除了 2 张卡！准备发奖励...");

                // 【终极修复】不再手动构建 CardModel 列表，改用 CardCreationOptions 让游戏自己生成奖励
                var cardPool = AccessTools.Property(Owner.Character.GetType(), "CardPool")?.GetValue(Owner.Character);
                var poolType = FindTypeByName("CardPoolModel");
                var poolsList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(poolType));
                poolsList.Add(cardPool);

                var rarityType = FindTypeByName("CardRarity");
                object rewardRarity = Enum.Parse(rarityType, targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" });

                var optionsType = FindTypeByName("CardCreationOptions");
                var sourceType = FindTypeByName("CardCreationSource");
                var oddsType = FindTypeByName("CardRarityOddsType");
                var cardModelType = FindTypeByName("CardModel");
                var cardRewardType = FindTypeByName("CardReward");

                // 创建过滤器委托，防止基础卡、诅咒等作为奖励出现
                var filterMethod = typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic);
                var filterDelegate = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool)), filterMethod);

                // 构造 CardCreationOptions，加入我们自己的过滤器
                object options = Activator.CreateInstance(optionsType, new object[] {
                    poolsList,
                    Enum.Parse(sourceType, "Other"),
                    Enum.Parse(oddsType, "Uniform"),
                    filterDelegate // 第四个参数是过滤器
                });

                // 使用万能构造函数匹配器实例化 CardReward
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
                            else if (p.ParameterType == typeof(int)) args.Add(1); // 数量1
                            else if (p.ParameterType == typeof(Player)) args.Add(Owner);
                            else if (p.ParameterType.IsValueType) args.Add(Activator.CreateInstance(p.ParameterType));
                            else args.Add(null);
                        }
                        reward = ctor.Invoke(args.ToArray());
                        if (reward != null) break;
                    }
                    catch { }
                }

                if (reward == null) { Godot.GD.Print("[MyFirstMod] 依然找不到匹配的 CardReward 构造函数"); return false; }

                var rewardsListType = typeof(List<>).MakeGenericType(FindTypeByName("Reward"));
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