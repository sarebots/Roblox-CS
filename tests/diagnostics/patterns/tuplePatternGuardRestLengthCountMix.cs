using System.Collections.Generic;

class TuplePatternGuardRestLengthCountMix
{
    public int Describe((List<int> Values, int Tail) input) => input switch
    {
        ([var head, .. var rest], var tail) when (rest?.Length ?? rest?.Count ?? 0) > head => tail,
        _ => 0,
    };
}
