using System.Collections.Generic;

class ListPatternSliceTuple
{
    public string Describe(List<int> values) =>
        values switch
        {
            [.. (var head, var tail)] => "invalid", // expect: Slice captures must bind a single identifier.
            _ => "none",
        };
}
