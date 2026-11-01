using System.Windows.Forms;

namespace Penumbra;

internal static class Program
{
    public const string ShowArgument = "--show";

    [STAThread]
    private static void Main(string[] args)
    {
        if (Diagnostics.TryRun(args))
            return;

        using var mutex = new Mutex(true, @"Local\Penumbra", out var firstInstance);
        if (!firstInstance)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(args.Contains(ShowArgument)));
    }
}
