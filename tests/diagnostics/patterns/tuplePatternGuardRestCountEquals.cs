using System.Collections.Generic;

class TuplePatternGuardRestCountEquals
{
    public int Describe((List<int> Values, int Tail) input) => input switch
    {
        ([var head, .. var rest], var tail) when rest.Count.Equals(head) => tail, // expect: Guard expressions cannot reference slice counts before bindings are established.
        _ => 0,
    };
}
