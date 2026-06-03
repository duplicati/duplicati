using System.CommandLine;

namespace TestDataGenerator;

public static class Program
{
    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("Test Data Generator")
        {
            TestDataGenerator.Commands.Create.CreateCommand(),
            TestDataGenerator.Commands.Update.CreateCommand()
        };

        return rootCommand.Invoke(args);
    }
}

