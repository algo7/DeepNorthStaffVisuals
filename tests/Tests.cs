using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Tests
{
    private static int Main()
    {
        var tests = typeof(Tests).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Test_", StringComparison.Ordinal))
            .OrderBy(m => m.MetadataToken)
            .ToList();
        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Invoke(null, null);
                System.Console.WriteLine($"PASS {test.Name.Substring(5)}");
            }
            catch (TargetInvocationException e)
            {
                failed++;
                System.Console.WriteLine($"FAIL {test.Name.Substring(5)}: {e.InnerException?.Message}");
            }
        }
        System.Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    private static void Eq<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }

    private static void True(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static void False(bool condition, string what) => True(!condition, what);
}
