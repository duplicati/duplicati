//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    public static class Runner
    {
        private class MessageSink : Duplicati.Library.Main.IMessageSink
        {
            #region IMessageSink implementation
            public void BackendEvent(Duplicati.Library.Main.BackendActionType action, Duplicati.Library.Main.BackendEventType type, string path, long size)
            {
            }
            public void VerboseEvent(string message, object[] args)
            {
            }
            public void MessageEvent(string message)
            {
            }
            public void RetryEvent(string message, Exception ex)
            {
            }
            public void WarningEvent(string message, Exception ex)
            {
            }
            public void ErrorEvent(string message, Exception ex)
            {
            }
            public void DryrunEvent(string message)
            {
            }
            public Duplicati.Library.Main.IBackendProgress BackendProgress
            {
                set
                {
                }
            }
            public Duplicati.Library.Main.IOperationProgress OperationProgress
            {
                set
                {
                }
            }
            #endregion
        }
    
        public static void Run(Tuple<long, Server.Serialization.DuplicatiOperation> item)
        {
            var backup = Program.DataConnection.GetBackup(item.Item1);
            if (backup == null)
            {
                //TODO: Log this
                return;
            }
            
            Program.ProgressEventNotifyer.SignalNewEvent();            
            var options = ApplyOptions(backup, item.Item2, GetCommonOptions(backup, item.Item2));
            var sink = new MessageSink();
            
            using(var controller = new Duplicati.Library.Main.Controller(backup.TargetURL, options, sink))
            {
                switch (item.Item2)
                {
                    case DuplicatiOperation.Backup:
                        {
                            var filter = ApplyFilter(backup, item.Item2, GetCommonFilter(backup, item.Item2));
                            var r = controller.Backup(backup.Sources, filter);
                            UpdateMetadata(backup, r);
                        }
                        break;
                        
                    case DuplicatiOperation.List:
                        {
                            //TODO: Need to pass arguments
                            var r = controller.List();
                            UpdateMetadata(backup, r);
                        }
                        break;
                    case DuplicatiOperation.Repair:
                        {
                            var r = controller.Repair();
                            UpdateMetadata(backup, r);
                        }
                        break;
                    case DuplicatiOperation.Remove:
                        {
                            var r = controller.Delete();
                            UpdateMetadata(backup, r);
                        }
                        break;
                    case DuplicatiOperation.Restore:
                        {
                            //TODO: Need to pass arguments
                            var r = controller.Restore(new string[] {"*"});
                            UpdateMetadata(backup, r);
                        }
                        break;
                    case DuplicatiOperation.Verify:
                        {
                            //TODO: Need to pass arguments
                            var r = controller.Test();
                            UpdateMetadata(backup, r);
                        }
                        break;
                    default:
                        //TODO: Log this
                        return;
                }
            }
            
        }
        
        private static void UpdateMetadata(Duplicati.Server.Serialization.Interface.IBackup backup, object o)
        {
            //TODO: Read the quota and other stuff here
        }
        
        private static bool TestIfOptionApplies(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, string filter)
        {
            //TODO: Implement to avoid warnings
            return true;
        }
        
        private static Dictionary<string, string> ApplyOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Dictionary<string, string> options)
        {
            foreach(var o in backup.Settings)
                if (TestIfOptionApplies(backup, mode, o.Filter))
                    options[o.Name] = o.Value;
        
            return options;
        }
        
        private static Duplicati.Library.Utility.FilterExpression ApplyFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode, Duplicati.Library.Utility.FilterExpression filter)
        {
            var f2 = backup.Filters;
            if (f2 != null && f2.Length > 0)
                filter = new Duplicati.Library.Utility.FilterExpression[] { filter }.Union(
                    (from n in f2
                    orderby n.Order
                    select new Duplicati.Library.Utility.FilterExpression(n.Expression, n.Include))
                )
                .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));
            
            return filter;
        }
        
        private static Dictionary<string, string> GetCommonOptions(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            return 
                (from n in Program.DataConnection.Settings
                where TestIfOptionApplies(backup, mode, n.Filter)
                select n).ToDictionary(k => k.Name, k => k.Value);
        }
        
        private static Duplicati.Library.Utility.FilterExpression GetCommonFilter(Duplicati.Server.Serialization.Interface.IBackup backup, DuplicatiOperation mode)
        {
            var filters = Program.DataConnection.Filters;
            if (filters == null || filters.Length == 0)
                return null;
            
           return   
                (from n in filters
                orderby n.Order
                select new Duplicati.Library.Utility.FilterExpression(n.Expression, n.Include))
                .Aggregate((a, b) => Duplicati.Library.Utility.FilterExpression.Combine(a, b));
        }
    }
}

