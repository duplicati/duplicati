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
                            select n.Substring("AllowedWeekDays=".Length).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
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

