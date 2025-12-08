using System;

public interface IIndexedInterface
{
    double this[int index] { get; set; }
}

public interface IRefInterface
{
    void Fill(ref int value); // expect: Interface methods with ref or out parameters are not supported yet.
    void Output(out int result); // expect: Interface methods with ref or out parameters are not supported yet.
}
