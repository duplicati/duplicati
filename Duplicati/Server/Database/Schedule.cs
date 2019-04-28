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
using System;
using System.Linq;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Server.Database
{
    public class Schedule : ISchedule
    {
        public long ID { get; set; }
        public string[] Tags { get; set; }
        public DateTime Time { get; set; }
        public string Repeat { get; set; }
        public DateTime LastRun { get; set; }
        public string Rule { get; set; }
        
        public DayOfWeek[] AllowedDays
        { 
            get
            {
                if (string.IsNullOrEmpty(this.Rule))
                    return null;
                    
                var days = (from n in this.Rule.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries) 
                           where n.StartsWith("AllowedWeekDays=", StringComparison.OrdinalIgnoreCase)
                           select n.Substring("AllowedWeekDays=".Length).Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries))
                           .FirstOrDefault();
                
                
                if (days == null)
                    return null;

                return (from n in days 
                        where Enum.TryParse<DayOfWeek>(n, true, out _)
                        select (DayOfWeek)Enum.Parse(typeof(DayOfWeek), n, true))
                        .ToArray();
            }
            set
            {
            
                var parts = 
                    string.IsNullOrEmpty(this.Rule) ? 
                    new string[0] :
                    (from n in this.Rule.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries) 
                               where !n.StartsWith("AllowedWeekDays=", StringComparison.OrdinalIgnoreCase)
                               select n);
                                                           
                if (value != null && value.Length != 0)
                    parts = parts.Union(new string[] {
                        "AllowedWeekDays=" +
                        string.Join(
                        ",",
                        (from n in value
                                select Enum.GetName(typeof(DayOfWeek), n)).Distinct()
                        )
                    }).Distinct();
                
                this.Rule = string.Join(";", parts);
            }
        }
    }
}

