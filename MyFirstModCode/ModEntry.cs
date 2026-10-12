using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class MyModEntry
{
    public const string ModId = "MyFirstMod";
    
    // 修改：写到游戏自己的私有目录，绝对不会被安卓拦截
    public static string LogPath = "/sdcard/Android/data/com.megacrit.sts2re/files/MyFirstMod_Debug.txt";

    public static void Initialize()
    {
        try
        {
            File.WriteAllText(LogPath, $"[{DateTime.Now}] ======== 开始初始化 ========\n");
            
            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            File.AppendAllText(LogPath, $"[{DateTime.Now}] ======== Harmony补丁应用成功 ========\n");
        }
        catch (Exception ex)
        {
            try {
                File.AppendAllText(LogPath, $"[{DateTime.Now}] ======== 初始化报错: {ex.Message} ========\n");
                File.AppendAllText(LogPath, $"[{DateTime.Now}] 堆栈: {ex.StackTrace}\n");
            } catch { }
        }
    }
}