public class ReservedIdentifierFixtures
{
    private int self; // expect: Identifier 'self' is reserved for Roblox Luau interop and cannot be used.

    public int _G { get; set; } // expect: Identifier '_G' is reserved for Roblox Luau interop and cannot be used.

    public void Configure(int _ENV) // expect: Identifier '_ENV' is reserved for Roblox Luau interop and cannot be used.
    {
        var super = 0; // expect: Identifier 'super' is reserved for Roblox Luau interop and cannot be used.
    }
}
