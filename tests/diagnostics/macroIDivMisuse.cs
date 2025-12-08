class MacroIDivMisuse
{
    public int MissingArgument(int value) => value.idiv(); // expect: [ROBLOXCS3012] idiv expects exactly one argument: the divisor.

    public int NonNumericArgument(int value) => value.idiv("two"); // expect: [ROBLOXCS3013] idiv requires both operands to be numeric types.

    public int NonNumericReceiver(string value) => value.idiv(2); // expect: [ROBLOXCS3013] idiv requires both operands to be numeric types.
}
