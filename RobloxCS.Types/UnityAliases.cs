using System;

namespace Roblox
{
    /// <summary>
    /// Provides aliases for Unity API calls to facilitate migration to Roblox.
    /// These methods are macros that transpile to direct Roblox API calls.
    /// </summary>
    public static class UnityAliases
    {
        // Maps to print() in Luau
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine($"[WARN] {message}");
        public static void LogError(object message) => Console.Error.WriteLine(message);
    }
}
