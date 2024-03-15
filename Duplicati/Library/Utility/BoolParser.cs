using Duplicati.Library.Utility.Abstractions;

namespace Duplicati.Library.Utility;

public class BoolParser : IBoolParser
{
    public bool ParseBool(string value, bool @default = false)
    {
        return Utility.ParseBool(value, @default); 
    }
}