using System;

namespace Roblox;

public class Instance
{
    public Instance()
    {
        ClassName = GetType().Name;
    }

    public string ClassName { get; protected set; }

    public Instance? Parent { get; set; }

    public virtual bool IsA(string className) => string.Equals(ClassName, className, StringComparison.Ordinal);

    public static T Create<T>(params object?[] _)
        where T : Instance, new()
    {
        return new T();
    }
}
