using System;
namespace Duplicati.Library.Backend
{
    public interface ICommandLineArgument
    {
        string[] Aliases { get; set; }
        string LongDescription { get; set; }
        string Name { get; set; }
        string ShortDescription { get; set; }
        CommandLineArgument.ArgumentType Type { get; set; }
        string[] ValidValues { get; set; }
    }
}
