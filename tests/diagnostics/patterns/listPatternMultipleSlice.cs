using System.Collections.Generic;

class ListPatternMultipleSlice
{
    public string Describe(List<int> values) =>
        values switch
        {
            [1, .. var middle, 4, .. var rest] => "invalid", // expect: List patterns support at most one slice pattern.
            _ => "none",
        };
}
