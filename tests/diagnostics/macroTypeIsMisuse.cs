class MacroTypeIsMisuse
{
    public bool MissingArgument(object value) => typeIs(value); // expect: [ROBLOXCS3010] typeIs expects exactly two arguments: the value to inspect and a string literal type name.

    public bool NonLiteralType(object value)
    {
        var kind = "string";
        return typeIs(value, kind); // expect: [ROBLOXCS3011] typeIs requires the second argument to be a string literal type name.
    }
}
