using System.Collections.Generic;

class ListPatternGuardRestPropertyPattern
{
    public int Describe(List<int> values) => values switch
    {
        [var head, .. var rest] when rest is { Count: > var threshold } => threshold, // expect: Guard expressions cannot reference slice counts before bindings are established.
        _ => 0,
    };
}
