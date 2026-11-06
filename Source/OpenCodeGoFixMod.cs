using HarmonyLib;
using Verse;

namespace RTEM.OpenCodeGoFix
{
    /// <summary>
    /// Mod 入口。RimWorld 在 CreateModClasses 阶段调用各 Mod 子类的构造函数，
    /// 那时所有已启用 mod 的程序集都已加载完，所以无论排序如何都能找到 RimTalk-ExpandMemory 的类型。
    /// </summary>
    public sealed class OpenCodeGoFixMod : Mod
    {
        public OpenCodeGoFixMod(ModContentPack content) : base(content)
        {
            Patches.Apply(new Harmony(Patches.HarmonyId));
        }
    }
}
