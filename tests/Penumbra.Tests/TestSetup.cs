using System.Runtime.CompilerServices;
using Penumbra.Config;

namespace Penumbra.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void DisableLog() => Log.Enabled = false;
}
