using System.Collections;
using System.Collections.Generic;

namespace Roblox;

public class LuaTuple : IEnumerable<object?>
{
    protected LuaTuple(params object?[] values)
    {
        Values = values;
    }

    protected object?[] Values { get; }

    public virtual IEnumerator<object?> GetEnumerator()
    {
        foreach (var value in Values)
            yield return value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class LuaTuple<T1> : LuaTuple
{
    public LuaTuple(T1 item1) : base(item1)
    {
        Item1 = item1;
    }

    public T1 Item1 { get; }

    public void Deconstruct(out T1 item1) => item1 = Item1;
}

public class LuaTuple<T1, T2> : LuaTuple
{
    public LuaTuple(T1 item1, T2 item2) : base(item1, item2)
    {
        Item1 = item1;
        Item2 = item2;
    }

    public T1 Item1 { get; }
    public T2 Item2 { get; }

    public void Deconstruct(out T1 item1, out T2 item2)
    {
        item1 = Item1;
        item2 = Item2;
    }
}

public class LuaTuple<T1, T2, T3> : LuaTuple
{
    public LuaTuple(T1 item1, T2 item2, T3 item3) : base(item1, item2, item3)
    {
        Item1 = item1;
        Item2 = item2;
        Item3 = item3;
    }

    public T1 Item1 { get; }
    public T2 Item2 { get; }
    public T3 Item3 { get; }

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3)
    {
        item1 = Item1;
        item2 = Item2;
        item3 = Item3;
    }
}
