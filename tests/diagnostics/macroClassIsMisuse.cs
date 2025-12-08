class MacroClassIsMisuse
{
    public bool MissingArgument(object value) => classIs(value); // expect: [ROBLOXCS3010] classIs expects exactly two arguments: the value to inspect and a string literal class name.

    public bool NonLiteralType(object value)
    {
        var kind = typeof(MacroClassIsMisuse).Name;
        return classIs(value, kind); // expect: [ROBLOXCS3011] classIs requires the second argument to be a string literal class name.
    }
}
