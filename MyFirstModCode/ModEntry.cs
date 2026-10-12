using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class MyModEntry
{
    public const string ModId = "MyFirstMod";
    
    public static void Initialize()
    {
        try
        {
            // 利用 Godot 引擎原生打印日志，确保能记录在游戏的 logcat 中
            Godot.GD.Print("[MyFirstMod] ======== 开始初始化 ========");
            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Godot.GD.Print("[MyFirstMod] ======== Harmony补丁应用成功 ========");
        }
        catch (Exception ex)
        {
            Godot.GD.Print($"[MyFirstMod] 初始化报错: {ex.Message}\n{ex.StackTrace}");
        }
    }
}