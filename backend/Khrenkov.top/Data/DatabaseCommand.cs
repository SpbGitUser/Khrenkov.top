namespace Khrenkov.top.Data;

internal sealed record DatabaseCommand(string Action, string? Target, string[] ConfigurationArguments)
{
    public static DatabaseCommand? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "database")
            return null;
        if (args.Length < 2 || args[1] is not ("migrate" or "rollback" or "status"))
            throw new ArgumentException("Usage: database migrate | database status | database rollback --to <migration|0>");
        if (args[1] != "rollback")
            return new DatabaseCommand(args[1], null, args[2..]);
        if (args.Length < 4 || args[2] != "--to" || args[3].StartsWith('-'))
            throw new ArgumentException("Usage: database rollback --to <migration|0>");
        return new DatabaseCommand("rollback", args[3], args[4..]);
    }
}
