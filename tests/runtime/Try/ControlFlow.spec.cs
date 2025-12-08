using System;
using System.Collections.Generic;

namespace Runtime.Try;

public static class ControlFlowSpec
{
    public static void ShouldReturnFromFinallyWithoutThrowing()
    {
        int Value()
        {
            try
            {
                throw new InvalidOperationException("boom");
            }
            finally
            {
            }
        }

        var result = Value();
        if (result != 42)
        {
            throw new Exception($"Expected finally return to win, got {result}");
        }
    }

    public static void ShouldBreakFromFinallyWithoutThrowing()
    {
        var iterations = 0;

        void Runner()
        {
            var attempts = 0;
            while (attempts < 3)
            {
                attempts++;
                try
                {
                    throw new InvalidOperationException("boom");
                }
                finally
                {
                    iterations++;
                }
            }
        }

        Runner();

        if (iterations != 1)
        {
            throw new Exception($"Expected loop to break after finally, iterations={iterations}");
        }
    }

    public static void ShouldContinueFromFinallyWithoutThrowing()
    {
        var iterations = 0;
        var runs = 0;

        void Runner()
        {
            for (var i = 0; i < 3; i++)
            {
                runs++;
                try
                {
                    throw new InvalidOperationException("boom");
                }
                finally
                {
                    iterations++;
                }
            }
        }

        Runner();

        if (runs != 3 || iterations != 3)
        {
            throw new Exception($"Expected continue in finally to keep looping, runs={runs}, iterations={iterations}");
        }
    }

    public static void ShouldRunTryCatchFinallyInOrder()
    {
        var order = new List<int>();

        try
        {
            var condition = true;
            try
            {
                order.Add(1);
                if (condition)
                {
                    throw new InvalidOperationException("boom");
                }
                order.Add(999);
            }
            catch
            {
                order.Add(2);
            }
            finally
            {
                order.Add(3);
            }
        }
        catch
        {
        }

        if (order.Count != 3 || order[0] != 1 || order[1] != 2 || order[2] != 3)
        {
            throw new Exception("Expected try/catch/finally ordering to be 1,2,3.");
        }
    }

    public static void ShouldRunTryFinallyInOrder()
    {
        var order = new List<int>();

        try
        {
            var condition = true;
            try
            {
                order.Add(1);
                if (condition)
                {
                    throw new InvalidOperationException("boom");
                }
                order.Add(999);
            }
            finally
            {
                order.Add(2);
            }
        }
        catch
        {
        }

        if (order.Count != 2 || order[0] != 1 || order[1] != 2)
        {
            throw new Exception("Expected try/finally ordering to be 1,2.");
        }
    }

    public static void ShouldRunFinallyEvenIfCatchThrows()
    {
        var ranFinally = false;

        try
        {
            try
            {
                throw new InvalidOperationException("try error");
            }
            catch
            {
                throw new InvalidOperationException("catch error");
            }
            finally
            {
                ranFinally = true;
            }
        }
        catch
        {
        }

        if (!ranFinally)
        {
            throw new Exception("Expected finally block to run even when catch rethrows.");
        }
    }

    public static void ShouldThrowIfFinallyThrows()
    {
        void ThrowingFinally()
        {
            try
            {
            }
            finally
            {
                throw new InvalidOperationException("boom");
            }
        }

        try
        {
            ThrowingFinally();
            throw new Exception("Expected finally to throw.");
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message != "boom")
            {
                throw new Exception("Unexpected finally exception payload.");
            }
        }
    }

    public static void ShouldSupportTryCatchWithMultipleFlowControlCases()
    {
        int Foo()
        {
            var x = 0;
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    if (i == 5)
                    {
                        return x;
                    }

                    if (i % 2 == 0)
                    {
                    }

                    x++;
                }
                catch
                {
                }
            }

            return x;
        }

        var result = Foo();
        if (result != 2)
        {
            throw new Exception($"Expected mixed control-flow to return 2, got {result}");
        }
    }

    public static void ShouldLoopBetweenNestedTryCatchWithBreak()
    {
        int Runner()
        {
            var x = 0;

            try
            {
                for (var i = 0; i < 10; i++)
                {
                    try
                    {
                        if (i == 5)
                        {
                            return x;
                        }

                        if (i % 2 == 0)
                        {
                        }

                        x++;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return x;
        }

        var value = Runner();
        if (value != 2)
        {
            throw new Exception($"Expected nested try/catch loop to return 2, got {value}");
        }
    }
}