using System;
using System.Collections.Generic;

namespace Verse
{
    public interface IExposable
    {
        void ExposeData();
    }

    public static class Log
    {
        public static readonly List<string> Messages = new List<string>();
        public static readonly List<string> Warnings = new List<string>();
        public static readonly List<string> Errors = new List<string>();

        public static void Message(string text) { Messages.Add(text); Console.WriteLine("      [Log.Message] " + text); }
        public static void Warning(string text) { Warnings.Add(text); Console.WriteLine("      [Log.Warning] " + text); }
        public static void Error(string text) { Errors.Add(text); Console.WriteLine("      [Log.Error] " + text); }
    }

    public class ModContentPack { }

    public abstract class Mod
    {
        public Mod(ModContentPack content) { }
    }
}
