public struct Buffer
{
    private int[] _values;

    public Buffer(int size)
    {
        _values = new int[size];
    }

    public int this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }
}
