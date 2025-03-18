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
using System.Runtime.Versioning;
using System.Security.Principal;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class HyperV : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/hyperv", () => Execute(null)).RequireAuthorization();
        group.MapGet("/hyperv/{key}", ([FromRoute] string key) => Execute(key)).RequireAuthorization();
    }

    private static IEnumerable<Dto.TreeNodeDto> Execute(string? key)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        return ExecuteOnWindows(key);
    }

    // Make sure the JIT does not attempt to inline this call and thus load
    // referenced types from System.Management here
    [SupportedOSPlatform("windows")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IEnumerable<Dto.TreeNodeDto> ExecuteOnWindows(string? key)
    {
        var hypervUtility = new HyperVUtility();

        if (!hypervUtility.IsHyperVInstalled || !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return [];

        try
        {
            if (string.IsNullOrEmpty(key))
            {
                hypervUtility.QueryHyperVGuestsInfo(SnapshotProvider.AlphaVSS);
                return hypervUtility.Guests.Select(x => new Dto.TreeNodeDto()
                {
                    id = x.ID.ToString(),
                    text = x.Name,
                    cls = "hyperv",
                    iconCls = "x-tree-icon-hyperv",
                    check = false,
                    temporary = false,
                    leaf = false,
                    systemFile = false,
                    hidden = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                }).ToList();
            }
            else
            {
                hypervUtility.QueryHyperVGuestsInfo(SnapshotProvider.AlphaVSS, true);
                var foundVMs = hypervUtility.Guests.FindAll(x => x.ID.Equals(new Guid(key)));

                if (foundVMs.Count != 1)
                    throw new NotFoundException(string.Format($"Cannot find VM with ID {key}"));
                return foundVMs[0].DataPaths.Select(x => new Dto.TreeNodeDto()
                {
                    text = x,
                    id = x,
                    cls = "folder",
                    iconCls = "x-tree-icon-hypervmachine",
                    check = false,
                    leaf = true,
                    hidden = false,
                    systemFile = false,
                    temporary = false,
                    symlink = false,
                    fileSize = -1,
                    resolvedpath = null
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to enumerate Hyper-V virtual machines: {ex.Message}", ex);
        }
    }
}
