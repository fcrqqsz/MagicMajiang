using System;
using System.Collections.Generic;

internal sealed class RegressionRunner
{
    private readonly List<string> _failures = new();
    private readonly HashSet<string> _executedSuites = new(StringComparer.OrdinalIgnoreCase);

    public void RunSuite(string name, Action<RegressionRunner> suite)
    {
        _executedSuites.Add(name);
        Console.WriteLine($"[suite] {name}");
        suite(this);
    }

    public void RequireSuite(string name)
    {
        Check(_executedSuites.Contains(name), $"Required regression suite was not executed: {name}");
    }

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
