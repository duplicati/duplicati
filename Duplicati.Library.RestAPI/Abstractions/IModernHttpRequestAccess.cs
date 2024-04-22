namespace Duplicati.Library.RestAPI.Abstractions;

public interface IModernHttpRequestAccess
{
    public string GetQueryParam(string name);
}