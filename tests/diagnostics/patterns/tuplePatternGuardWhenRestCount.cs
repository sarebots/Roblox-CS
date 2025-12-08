using System.Collections.Generic;

class TuplePatternGuardWhenRestCount
{
    public int Describe(List<int> values) => values switch
    {
        var tuple when tuple is [var head, .. var rest, var tail] && rest.Count > head => tail, // expect: Guard expressions cannot reference slice counts before bindings are established.
        _ => 0,
    };
}
