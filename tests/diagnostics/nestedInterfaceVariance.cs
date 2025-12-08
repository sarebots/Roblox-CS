public interface IBaseInterface
{
    int Compute(int value);
    string Name { get; }
}

public class Container
{
    public interface DerivedInterface : IBaseInterface
    {
        double Compute(double value); // expect: Nested interface member 'Compute' conflicts with inherited member signature.
        string Name { get; set; } // expect: Nested interface member 'Name' conflicts with inherited member signature.
    }
}
