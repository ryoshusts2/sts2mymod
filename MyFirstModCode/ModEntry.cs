using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class MyModEntry
{
    public const string ModId = "MyFirstMod";
    
    // 修正：改为写入 DLL 所在目录，避免安卓 10+ 系统的公共目录写入权限崩溃
    public static string LogPath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", 
        "MyFirstMod_Debug.txt"
    );

    public static void Initialize()
    {
        try
        {
            // 尝试清空并写入日志
            File.WriteAllText(LogPath, $"[{DateTime.Now}] ======== 开始初始化 ========\n");
            
            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            
            File.AppendAllText(LogPath, $"[{DateTime.Now}] ======== Harmony补丁应用成功 ========\n");
        }
        catch (Exception ex)
        {
            // 防止因写日志失败导致 DLL 直接崩溃
            try {
                File.AppendAllText(LogPath, $"[{DateTime.Now}] ======== 报错: {ex.Message} ========\n");
                File.AppendAllText(LogPath, $"[{DateTime.Now}] 堆栈: {ex.StackTrace}\n");
            } catch { }
            
            // 同时在控制台输出错误（如果你用 Termux 看 logcat 可以看到）
            Console.WriteLine($"[MyFirstMod] 初始化报错: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
