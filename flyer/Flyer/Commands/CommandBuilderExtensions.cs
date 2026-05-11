namespace Flyer.Commands;

using Smart.CommandLine.Hosting;

public static class CommandBuilderExtensions
{
    public static void AddCommands(this ICommandBuilder commands)
    {
        commands.AddCommand<LoadCommand>();
        commands.AddCommand<ListCommand>();
        commands.AddCommand<CheckCommand>();
        commands.AddCommand<TestCommand>();
    }
}
