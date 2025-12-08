class TrySwitchBreak
{
    public void Run(int value)
    {
        switch (value)
        {
            case 1:
                try
                {
                    break; // expect: [ROBLOXCS3021] break statements inside try/using blocks cannot exit switch statements.
                }
                finally
                {
                    value++;
                }
                break;
        }
    }
}
