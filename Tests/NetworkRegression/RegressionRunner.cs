using System;
using System.Collections.Generic;

internal sealed class RegressionRunner
{
    private readonly List<string> _failures = new();

    public void Check(bool condition, string message)
    {
        if (!condition) _failures.Add(message);
    }

    public int Complete()
    {
        if (_failures.Count == 0)
        {
            Console.WriteLine("Network regression tests passed.");
            return 0;
        }

        Console.Error.WriteLine(string.Join(Environment.NewLine, _failures));
        return 1;
    }
}
