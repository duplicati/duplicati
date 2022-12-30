//  Copyright (C) 2023, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

using Duplicati.Server.WebServer.RESTMethods;

namespace Duplicati.WebserverCorer
{
    public class RESTHandlerCore
    {
        public const string API_URI_PATH = "/api/v1";
        public static readonly int API_URI_SEGMENTS = API_URI_PATH.Split(new char[] { '/' }).Length;

        private static readonly Dictionary<string, IRESTMethod> _modules = new Dictionary<string, IRESTMethod>(StringComparer.OrdinalIgnoreCase);

        public static IDictionary<string, IRESTMethod> Modules { get { return _modules; } }

        /// <summary>
        /// Loads all REST modules in the Duplicati.Server.WebServer.RESTMethods namespace
        /// </summary>
        static RESTHandlerCore()
        {
            var lst =
                from n in typeof(IRESTMethod).Assembly.GetTypes()
                where
                    n.Namespace == typeof(IRESTMethod).Namespace
                    &&
                    typeof(IRESTMethod).IsAssignableFrom(n)
                    &&
                    !n.IsAbstract
                    &&
                    !n.IsInterface
                select n;

            foreach (var t in lst)
            {
                var m = (IRESTMethod)Activator.CreateInstance(t);
                _modules.Add(t.Name.ToLowerInvariant(), m);
            }
        }
    }
}

