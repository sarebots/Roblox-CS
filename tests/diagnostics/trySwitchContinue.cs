class TrySwitchContinue
{
    public void Run(int value)
    {
        while (value < 5)
        {
            switch (value)
            {
                case 1:
                    try
                    {
                        continue; // expect: [ROBLOXCS3022] Continue statements inside try/using blocks cannot exit switch statements.
                    }
                    finally
                    {
                        value++;
                    }
                default:
                    break;
            }

            value++;
        }
    }
}
