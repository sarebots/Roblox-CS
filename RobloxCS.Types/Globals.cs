using System;
using System.Collections.Generic;

namespace Roblox;

public static class Globals
{
    public static void print(params object?[] values)
    {
        // Runtime behavior handled in Luau runtime; this stub is compile-time only.
    }

    public static LuaTuple<bool, object?> pcall(Action callback)
    {
        try
        {
            callback();
            return new LuaTuple<bool, object?>(true, null);
        }
        catch (Exception ex)
        {
            return new LuaTuple<bool, object?>(false, ex);
        }
    }

    public static LuaTuple<bool, TResult?> pcall<TResult>(Func<TResult?> callback)
    {
        try
        {
            return new LuaTuple<bool, TResult?>(true, callback());
        }
        catch
        {
            return new LuaTuple<bool, TResult?>(false, default);
        }
    }

    public static IEnumerable<LuaTuple<int, T>> pairs<T>(IEnumerable<T> sequence)
    {
        var index = 0;
        foreach (var value in sequence)
        {
            yield return new LuaTuple<int, T>(index, value);
            index++;
        }
    }

    public static IEnumerable<LuaTuple<int, T>> ipairs<T>(IEnumerable<T> sequence) => pairs(sequence);

    public static bool typeIs(object? value, string typeName) => throw new NotImplementedException();

    public static bool classIs(object? value, string className) => throw new NotImplementedException();

    public static IEnumerable<double> range(double start, double finish) =>
        throw new NotSupportedException("Roblox.Globals.range is only available during transpilation.");

    public static IEnumerable<double> range(double start, double finish, double step) =>
        throw new NotSupportedException("Roblox.Globals.range is only available during transpilation.");

    public static LuaTuple tuple(params object?[] values) =>
        throw new NotSupportedException("Roblox.Globals.tuple is only available during transpilation.");

    public static LuaTuple<T1> tuple<T1>(T1 item1) =>
        throw new NotSupportedException("Roblox.Globals.tuple is only available during transpilation.");

    public static LuaTuple<T1, T2> tuple<T1, T2>(T1 item1, T2 item2) =>
        throw new NotSupportedException("Roblox.Globals.tuple is only available during transpilation.");

    public static LuaTuple<T1, T2, T3> tuple<T1, T2, T3>(T1 item1, T2 item2, T3 item3) =>
        throw new NotSupportedException("Roblox.Globals.tuple is only available during transpilation.");
}
