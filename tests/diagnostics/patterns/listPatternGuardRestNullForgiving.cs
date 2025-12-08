using System.Collections.Generic;

class ListPatternGuardRestNullForgiving
{
    public int Describe(List<int> values) => values switch
    {
        [var head, .. var rest, var tail] when rest!.Count > head => tail, // expect: Guard expressions cannot reference slice counts before bindings are established.
        _ => 0,
    };
}
