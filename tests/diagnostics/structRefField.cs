using System;

public struct Container
{
    public Span<int> Buffer; // expect: [ROBLOXCS3044] Struct fields, properties, or indexers of ref-like types are not supported yet.
}
