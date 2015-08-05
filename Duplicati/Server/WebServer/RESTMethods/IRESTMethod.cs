//  Copyright (C) 2015, The Duplicati Team
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
using System;using System.Collections.Generic;

namespace Duplicati.Server.WebServer.RESTMethods
{
    public interface IRESTMethod
    {    }    public interface IRESTMethodDocumented    {        string Description { get; }        IEnumerable<KeyValuePair<string, Type>> Types { get; }    }    public interface IRESTMethodGET : IRESTMethod    {        void GET(string key, RequestInfo info);    }
    public interface IRESTMethodPUT : IRESTMethod    {        void PUT(string key, RequestInfo info);    }    public interface IRESTMethodPOST : IRESTMethod    {        void POST(string key, RequestInfo info);    }    public interface IRESTMethodDELETE : IRESTMethod    {        void DELETE(string key, RequestInfo info);    }    public interface IRESTMethodPATCH : IRESTMethod    {        void PATCH(string key, RequestInfo info);    }}

