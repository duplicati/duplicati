namespace Duplicati.Library.Utility.Abstractions;

public interface IBoolParser
{
    bool ParseBool(string value, bool @default = false);
}