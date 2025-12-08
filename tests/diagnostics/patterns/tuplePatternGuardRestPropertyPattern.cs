class TuplePatternGuardRestPropertyPattern
{
    public int Describe((int[] Values, int Tail) input) => input switch
    {
        ([var head, .. var rest], var tail) when rest is { Length: > head } => tail, // expect: Guard expressions cannot reference slice counts before bindings are established.
        _ => 0,
    };
}
