using System;
using System.Collections.Generic;

namespace Roblox;

public static class TS
{
    public static IEnumerable<T> iter<T>(IEnumerable<T> source) =>
        throw new NotSupportedException("Roblox.TS.iter is only available during transpilation.");

    public static IEnumerable<T> array_flatten<T>(IEnumerable<IEnumerable<T>> sources) =>
        throw new NotSupportedException("Roblox.TS.array_flatten is only available during transpilation.");
}
