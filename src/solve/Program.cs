using Solve.UI;

namespace Solve;

/// <summary>
/// Entry point. Builds solve.exe, as the brief requires.
/// Everything the user does goes through the menu; keep this file thin.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            new MainMenu().Run();
            return 0;
        }
        catch (Exception ex)
        {
            // Last line of defence. Nothing should ever reach here - user-facing failures
            // are LpException and are handled inside the menu.
            Console.Error.WriteLine($"Unexpected failure: {ex.Message}");
            return 1;
        }
    }
}
