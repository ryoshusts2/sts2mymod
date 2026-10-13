using System;
using System.IO;
using System.Linq;
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
            Godot.GD.Print("[MyFirstMod] ======== 开始初始化 ========");
            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Godot.GD.Print("[MyFirstMod] ======== Harmony补丁应用成功 ========");

            // 执行图片 API 探测
            TestImageAPI();
        }
        catch (Exception ex)
        {
            Godot.GD.Print($"[MyFirstMod] 初始化报错: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void TestImageAPI()
    {
        try
        {
            Godot.GD.Print("[MyFirstMod] ======== 开始测试图片 API ========");
            
            // 1. 列出 Godot.Image 中所有包含 Load 或 Create 的方法，看看有哪些可用的加载器
            var methods = typeof(Godot.Image).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name.Contains("Load") || m.Name.Contains("Create"))
                .Select(m => m.Name)
                .Distinct()
                .ToList();
            
            foreach (var m in methods)
            {
                Godot.GD.Print($"[MyFirstMod] Image 可用方法: {m}");
            }

            // 2. 准备一个标准的 1x1 透明 PNG 的二进制数据（这是确定存在的一张极小图）
            byte[] pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
            
            bool success = false;

            // 3. 尝试静态方法 LoadPngFromBuffer
            var staticMethod = typeof(Godot.Image).GetMethod("LoadPngFromBuffer", BindingFlags.Public | BindingFlags.Static);
            if (staticMethod != null)
            {
                var img = staticMethod.Invoke(null, new object[] { pngBytes }) as Godot.Image;
                if (img != null)
                {
                    var tex = Godot.ImageTexture.CreateFromImage(img);
                    Godot.GD.Print($"[MyFirstMod] 成功【静态】LoadPngFromBuffer 创建纹理，尺寸: {tex.GetWidth()}x{tex.GetHeight()}");
                    success = true;
                }
            }

            // 4. 如果静态方法不行，尝试实例方法
            if (!success)
            {
                var instanceMethod = typeof(Godot.Image).GetMethod("LoadPngFromBuffer", BindingFlags.Public | BindingFlags.Instance);
                if (instanceMethod != null)
                {
                    var img = new Godot.Image();
                    var error = (Godot.Error)instanceMethod.Invoke(img, new object[] { pngBytes });
                    if (error == Godot.Error.Ok)
                    {
                        var tex = Godot.ImageTexture.CreateFromImage(img);
                        Godot.GD.Print($"[MyFirstMod] 成功【实例】LoadPngFromBuffer 创建纹理，尺寸: {tex.GetWidth()}x{tex.GetHeight()}");
                        success = true;
                    }
                    else
                    {
                        Godot.GD.Print($"[MyFirstMod] 实例方法 LoadPngFromBuffer 报错码: {error}");
                    }
                }
            }

            if (!success)
            {
                Godot.GD.Print("[MyFirstMod] 致命: Godot.Image 中找不到 LoadPngFromBuffer 方法！无法加载 Base64 图片！");
            }
        }
        catch (Exception ex)
        {
            Godot.GD.Print($"[MyFirstMod] 测试图片 API 崩溃: {ex.Message}\n{ex.StackTrace}");
        }
    }
}