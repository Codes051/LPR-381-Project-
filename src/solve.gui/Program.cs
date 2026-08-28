namespace Solve.Gui;

/// <summary>
/// Entry point for the window. The console entry point in the solve project is untouched.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
