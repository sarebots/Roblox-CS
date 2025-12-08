class MacroTupleMisuse
{
    public object Describe()
    {
        var pair = tuple(1, 2); // expect: [ROBLOXCS3015] tuple can only be used directly inside a return statement.
        return pair;
    }
}
