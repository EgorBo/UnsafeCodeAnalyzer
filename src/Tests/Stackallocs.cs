﻿using System.Runtime.CompilerServices;

internal unsafe class Stackallocs
{
    void ConstStackalloc(int len)
    {
        int* b = stackalloc int[len];
        Consume(b);
    }

    const int N = 1000;

    void NamedConstStackalloc(int len)
    {
        int* b = stackalloc int[N];
        Consume(b);
    }

    void UnboundStackalloc(int len)
    {
        int* b = stackalloc int[len];
        Consume(b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Consume(int* b)
    {
    }
}