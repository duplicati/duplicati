using System.Reflection;
using TLSharp.Core;

namespace Duplicati.Library.Backend
{
    public static class TlSharpExtensions
    {
        public static void SetDataCenterIpAddress(this Session session, string ipAddress, int port)
        {
            var allBindingFlags = (BindingFlags)~1024;
            var dcPropInfo = typeof(Session).GetProperty("DataCenter", allBindingFlags);
            var dataCenter = dcPropInfo.GetValue(session);
            
            var addressPropInfo = dataCenter.GetType().GetField("<Address>k__BackingField", allBindingFlags);
            var portPropInfo = dataCenter.GetType().GetField("<Port>k__BackingField", allBindingFlags);
            addressPropInfo.SetValue(dataCenter, ipAddress);
            portPropInfo.SetValue(dataCenter, port);
        }
    }
}