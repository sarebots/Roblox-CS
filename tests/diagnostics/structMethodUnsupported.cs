public struct Worker
{
    public int Value;

    public void Increment() // expect: [ROBLOXCS3043] Struct methods are not supported yet. Move 'Increment' to a class.
    {
        Value += 1;
    }
}
