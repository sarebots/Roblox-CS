using System;

public struct Buffer
{
    public Span<int> this[int index] // expect: [ROBLOXCS3044] Struct fields, properties, or indexers of ref-like types are not supported yet.
    {
        get => Span<int>.Empty;
        set { }
    }
}
