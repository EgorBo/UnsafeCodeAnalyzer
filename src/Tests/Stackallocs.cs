using System.Runtime.CompilerServices;

internal unsafe class Stackallocs
{
    void ConstStackalloc(int len)
    {
        int* b = stackalloc int[128];
        Consume(b);
    }

    void ConstStackalloc2(int len)
    {
        int* b = stackalloc int[sizeof(IntPtr)];
        Consume(b);
    }
    void ConstStackalloc3(int len)
    {
        const int N = 1000;
        int* b = stackalloc int[N + 2];
        Consume(b);
    }

    void ConstStackalloc4(int len)
    {
        IntPtr* b = stackalloc IntPtr[] { len, len };
        Consume(b);
    }

    const int N = 1000;

    void NamedConstStackalloc(int len)
    {
        int* b = stackalloc int[N];
        Consume(b);
    }

    void NamedConstStackalloc2(int len)
    {
        int* b = stackalloc int[Constants.ConstSize];
        Consume(b);
    }

    void UnboundStackalloc(int len)
    {
        int* b = stackalloc int[len];
        Consume(b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Consume<T>(T* b)
    {
    }
}
