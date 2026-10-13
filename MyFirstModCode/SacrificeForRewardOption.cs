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

            var cardModelType = FindTypeByName("CardModel");
            List<object> selectedCards = null;
            try
            {
                var prefsType = FindTypeByName("CardSelectorPrefs");
                if (prefsType == null) { Godot.GD.Print("[MyFirstMod] 找不到 CardSelectorPrefs"); return false; }

                var promptProp = AccessTools.Property(prefsType, "RemoveSelectionPrompt");
                object prompt = promptProp?.GetValue(null) ?? new LocString("rest_site_ui", "选择卡牌删除");

                // ============================================================
                // 【Bug 1 修复】允许返回 / 取消
                // 源码依据：CardSelectorPrefs 是 struct，有 init 属性 Cancelable
                //           NDeckCardSelectScreen._Ready() 中：
                //               if (_prefs.Cancelable) _closeButton.Enable();
                //               else                   _closeButton.Disable();
                // 因此必须设置 Cancelable = true，否则返回按钮永远禁用。
                // MinSelect = 0 让"确认"按钮在 0 张时也可点，但不是返回按钮的控制项。
                // ============================================================
                object prefs = null;
                try
                {
                    prefs = Activator.CreateInstance(prefsType, new object[] { prompt, 0, 2 });
                }
                catch (Exception ex)
                {
                    Godot.GD.Print($"[MyFirstMod] 构造 CardSelectorPrefs 失败: {ex.Message}");
                    return false;
                }
                if (prefs == null) return false;

                var cancelableProp = AccessTools.Property(prefsType, "Cancelable");
                if (cancelableProp != null && cancelableProp.CanWrite)
                {
                    try
                    {
                        cancelableProp.SetValue(prefs, true);
                    }
                    catch (Exception ex)
                    {
                        Godot.GD.Print($"[MyFirstMod] 设置 Cancelable=true 失败，尝试写 backing field: {ex.Message}");
                        var backingField = AccessTools.Field(prefsType, "<Cancelable>k__BackingField");
                        if (backingField != null) backingField.SetValue(prefs, true);
                    }
                }
                else
                {
                    Godot.GD.Print("[MyFirstMod] 警告：找不到 CardSelectorPrefs.Cancelable 可写属性，返回按钮可能不出现");
                }

                // 打印当前 prefs 内容，方便诊断
                try
                {
                    var minProp = AccessTools.Property(prefsType, "MinSelect");
                    var maxProp = AccessTools.Property(prefsType, "MaxSelect");
                    var cancelProp2 = AccessTools.Property(prefsType, "Cancelable");
                    var reqProp = AccessTools.Property(prefsType, "RequireManualConfirmation");
                    Godot.GD.Print($"[MyFirstMod] CardSelectorPrefs: Min={minProp?.GetValue(prefs)} Max={maxProp?.GetValue(prefs)} Cancelable={cancelProp2?.GetValue(prefs)} RequireManualConfirmation={reqProp?.GetValue(prefs)}");
                }
                catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打印 prefs 失败: {ex.Message}"); }

                // UI过滤：FromDeckForRemoval 支持 filter 参数
                var funcType = typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool));
                var parameter = Expression.Parameter(cardModelType, "card");
                var methodCall = Expression.Call(typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic), Expression.Convert(parameter, typeof(object)));
                var filterDelegate = Expression.Lambda(funcType, methodCall, parameter).Compile();

                var methods = AccessTools.GetDeclaredMethods(typeof(CardSelectCmd))
                    .Where(m => m.Name == "FromDeckForRemoval")
                    .OrderBy(m => m.GetParameters().Length)
                    .ToList();

                foreach (var method in methods)
                {
                    var ps = method.GetParameters();
                    var args = new List<object> { Owner, prefs };

                    if (ps.Length >= 3 && ps[2].ParameterType.Name.Contains("Func"))
                    {
                        args.Add(filterDelegate);
                    }
                    else
                    {
                        for (int i = 2; i < ps.Length; i++)
                        {
                            if (ps[i].ParameterType.IsValueType) args.Add(Activator.CreateInstance(ps[i].ParameterType));
                            else args.Add(null);
                        }
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
                                Godot.GD.Print($"[MyFirstMod] 拿到 {selectedCards.Count} 张牌（UI已过滤）");
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Godot.GD.Print($"[MyFirstMod] 打开选卡失败: {ex.Message}\n{ex.StackTrace}"); return false; }

            // 严格判断选牌数量，必须正好是 2 张
            if (selectedCards == null || selectedCards.Count != 2)
            {
                Godot.GD.Print($"[MyFirstMod] 玩家取消了操作或未选满2张（当前选了 {selectedCards?.Count ?? 0} 张），安全返回篝火。");
                return false;
            }

            foreach (var card in selectedCards)
            {
                if (!IsValidCard(card)) return false;
            }

            try
            {
                // 获取卡牌权重（包含升级状态检查：敲过的卡 +0.5）
                double w1 = GetCardWeight(selectedCards[0]);
                double w2 = GetCardWeight(selectedCards[1]);

                // 计算加权平均值
                double avgWeight = (w1 + w2) / 2.0;

                // 【五舍六入】小数部分 <= 0.5 时舍去，> 0.5 时进一。等价于 Math.Floor(avg + 0.4)
                int targetQ = (int)Math.Floor(avgWeight + 0.4);

                if (targetQ < 1) targetQ = 1;
                if (targetQ > 4) targetQ = 4;

                string targetRarityStr = targetQ switch { 1 => "Common", 2 => "Uncommon", 3 => "Rare", 4 => "Ancient", _ => "Common" };
                Godot.GD.Print($"[MyFirstMod] 两张卡调整后权重为 {w1} 和 {w2}，平均值为 {avgWeight}，五舍六入后目标品质为 {targetQ}（{targetRarityStr}）");

                // 删卡
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
                Godot.GD.Print($"[MyFirstMod] 成功删除了 2 张卡");

                // 发奖励
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

                var rewardParam = Expression.Parameter(cardModelType, "card");
                var isRewardValidCall = Expression.Call(typeof(SacrificeForRewardOption).GetMethod(nameof(IsValidCard), BindingFlags.Static | BindingFlags.NonPublic), Expression.Convert(rewardParam, typeof(object)));
                var rarityProp = Expression.Property(rewardParam, "Rarity");
                var targetRarityConst = Expression.Constant(rewardRarity, rarityProp.Type);
                var rarityCheck = Expression.Equal(rarityProp, targetRarityConst);
                var rewardBody = Expression.AndAlso(isRewardValidCall, rarityCheck);
                var rewardFilterDelegate = Expression.Lambda(typeof(Func<,>).MakeGenericType(cardModelType, typeof(bool)), rewardBody, rewardParam).Compile();

                object options = Activator.CreateInstance(optionsType, new object[] {
                    poolsList, Enum.Parse(sourceType, "Other"), Enum.Parse(oddsType, "Uniform"), rewardFilterDelegate
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
                Godot.GD.Print($"[MyFirstMod] 发奖报错: {ex.Message}\n{ex.StackTrace}");
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
                if (rarityStr != "Common" && rarityStr != "Uncommon" && rarityStr != "Rare" && rarityStr != "Ancient" && rarityStr != "Basic") return false;

                return true;
            }
            catch { return false; }
        }

        // ============================================================
        // 获取卡牌权重
        // 源码依据：
        //   CardModel.Rarity: public virtual CardRarity Rarity { get; }
        //   CardModel.CurrentUpgradeLevel: public int CurrentUpgradeLevel { get; set; }
        //   CardModel.IsUpgraded: public bool IsUpgraded => CurrentUpgradeLevel > 0;
        //   CardRarity 枚举: None, Basic, Common, Uncommon, Rare, Ancient, Event, Token, Status, Curse, Quest
        // 【Bug 2 修复】删除了错误的 "?? AccessTools.Property(t, \"Upgraded\")"
        //              —— Upgraded 是 CardModel 上的事件（event Action? Upgraded），不是属性
        //  同时加入调试日志，打印实际读到的稀有度 / 等级 / 最终权重
        // ============================================================
        private static double GetCardWeight(object card)
        {
            if (card == null) return 1.0;
            try
            {
                Type t = card.GetType();

                string rarityStr = AccessTools.Property(t, "Rarity")?.GetValue(card)?.ToString() ?? "Unknown";
                double baseWeight = rarityStr switch
                {
                    "Basic" => 1,
                    "Common" => 1,
                    "Uncommon" => 2,
                    "Rare" => 3,
                    "Ancient" => 4,
                    _ => 1
                };

                bool isUpgraded = false;
                int upgradeLevel = 0;

                var isUpgradedProp = AccessTools.Property(t, "IsUpgraded");
                if (isUpgradedProp != null)
                {
                    var val = isUpgradedProp.GetValue(card);
                    if (val is bool b) isUpgraded = b;
                }

                var upgradeLevelProp = AccessTools.Property(t, "CurrentUpgradeLevel");
                if (upgradeLevelProp != null)
                {
                    var lvlVal = upgradeLevelProp.GetValue(card);
                    if (lvlVal is int i) upgradeLevel = i;
                }

                double result = baseWeight + (isUpgraded ? 0.5 : 0);

                Godot.GD.Print($"[MyFirstMod] GetCardWeight: rarity={rarityStr} base={baseWeight} CurrentUpgradeLevel={upgradeLevel} IsUpgraded={isUpgraded} => final={result}");

                return result;
            }
            catch (Exception ex)
            {
                Godot.GD.Print($"[MyFirstMod] GetCardWeight 报错: {ex.Message}");
                return 1.0;
            }
        }
    }
}