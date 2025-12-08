using System.Collections.Generic;

class ListPatternDiscardHeadTail
{
    public string Describe(List<int> values) =>
        values switch
        {
            [_, .. var rest, _] => $"rest:{rest.Count}", // expect: Discard patterns cannot surround slice captures in list patterns.
            _ => "none",
        };
}
