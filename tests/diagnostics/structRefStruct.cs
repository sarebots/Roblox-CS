using System;

public ref struct Buffer // expect: [ROBLOXCS3042] ref struct declarations are not supported yet.
{
    public Span<int> Data;
}
