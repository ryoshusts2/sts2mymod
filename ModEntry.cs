using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class MyModEntry
{
    public const string ModId = "MyFirstMod";
    
    // 注意：日志会生成在 DLL 所在目录
    public static string LogPath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", 
        "MyFirstMod_Debug.txt"
    );

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
                File.AppendAllText(LogPath, $"[{DateTime.Now}] ======== 报错: {ex.Message} ========\n");
                File.AppendAllText(LogPath, $"[{DateTime.Now}] 堆栈: {ex.StackTrace}\n");
            } catch { }
            
            Console.WriteLine($"[MyFirstMod] 初始化报错: {ex.Message}\n{ex.StackTrace}");
        }
    }
}