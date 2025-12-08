using System;

public struct Holder
{
    public Span<int> Data { get; set; } // expect: [ROBLOXCS3044] Struct fields or properties of ref-like types are not supported yet.
}
