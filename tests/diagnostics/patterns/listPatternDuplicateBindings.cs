using System.Collections.Generic;

class ListPatternDuplicateBindings
{
    public string Describe(List<int> values) => values switch
    {
        [var head, var head, .. var _] => "invalid", // expect: Variable 'head' is bound multiple times in the same list pattern.
        _ => "ok",
    };
}
