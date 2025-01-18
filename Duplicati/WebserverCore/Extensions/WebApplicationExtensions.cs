// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
using System.Reflection;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Middlewares;

namespace Duplicati.WebserverCore.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication AddEndpoints(this WebApplication application, bool useCors)
    {
        return AddV1(application, useCors);
    }

    private static WebApplication AddV1(WebApplication application, bool useCors)
    {
        var mapperInterfaceType = typeof(IEndpointV1);
        var endpoints =
            typeof(WebApplicationExtensions).Assembly.DefinedTypes
                .Where(t => t.ImplementedInterfaces.Contains(mapperInterfaceType))
                .ToArray();

        var group = application.MapGroup("/api/v1")
            .AddEndpointFilter<LanguageFilter>()
            .AddEndpointFilter<HostnameFilter>();

        if (!string.IsNullOrWhiteSpace(PreSharedKeyFilter.PreSharedKey))
            group = group.AddEndpointFilter<PreSharedKeyFilter>();

        if (useCors)
            group.RequireCors(DuplicatiWebserver.CorsPolicyName);

        foreach (var endpoint in endpoints)
        {
            var methodMap = endpoint.GetMethod(nameof(IEndpointV1.Map), BindingFlags.Static | BindingFlags.Public);
            methodMap!.Invoke(null, [group]);
        }

        return application;
    }
}