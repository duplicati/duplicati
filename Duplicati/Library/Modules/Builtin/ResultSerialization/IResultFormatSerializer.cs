namespace Duplicati.Library.Modules.Builtin
{
    public interface IResultFormatSerializer
    {
        string Serialize(object result);
    }
}
