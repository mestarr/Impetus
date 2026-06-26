namespace Impetus.Cli;

static class RepoPaths
{
    public static string Root()
    {
        string? strDir = AppContext.BaseDirectory;
        while (strDir is not null)
        {
            if (Directory.Exists(Path.Combine(strDir, "specs"))
                || Directory.Exists(Path.Combine(strDir, ".git")))
                return strDir;
            strDir = Path.GetDirectoryName(strDir);
        }
        return Directory.GetCurrentDirectory();
    }

    public static string DefaultSpec()
        => Path.Combine(Root(), "specs", "demo-1kN.json");

    public static string DesignDir(string strName)
        => Path.Combine(Root(), "designs", strName);
}
