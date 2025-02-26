using System.CommandLine;

var rootCommand = new RootCommand("Test Data Generator")
{
    TestDataGenerator.Commands.Create.CreateCommand(),
    TestDataGenerator.Commands.Update.CreateCommand()
};

return rootCommand.Invoke(args);

