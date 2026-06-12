namespace RfcRag.Cli;

internal static class CliArgParser
{
    public static int ParseIntFlag(string[] args, string flag, int defaultValue)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int value))
            return value;
        return defaultValue;
    }

    public static string? ParseStringFlag(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length)
            return args[idx + 1];
        return null;
    }

    public static string ParseStringFlag(string[] args, string flag, string defaultValue)
    {
        int idx = Array.IndexOf(args, flag);
        if (idx >= 0 && idx + 1 < args.Length)
            return args[idx + 1];
        return defaultValue;
    }
}
