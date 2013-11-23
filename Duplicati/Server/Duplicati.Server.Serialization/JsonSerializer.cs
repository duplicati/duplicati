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
using Newtonsoft.Json.Serialization;
using System.Reflection;
using Newtonsoft.Json;
using System.Globalization;
using System.Collections.Generic;

namespace Duplicati.Server.Serialization
{
	internal class JsonSerializer : DefaultContractResolver
	{
        static JsonSerializer()
        {
        }

        private static string MemberInfoAsKey(MemberInfo mi) { return mi.DeclaringType.FullName + "!" + mi.Name; }
        private static string ExposedNamed(MemberInfo mi)
        {
            /*string newname;
            if (!m_renameList.TryGetValue(MemberInfoAsKey(mi), out newname))
                newname = mi.Name;
            return newname;*/
            return mi.Name;
        }

        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            //return base.GetSerializableMembers(objectType).Where(x => !m_ignoreList.ContainsKey(MemberInfoAsKey(x))).ToList();
            return base.GetSerializableMembers(objectType);
        }

        protected override JsonObjectContract CreateObjectContract (Type objectType)
        {
            return base.CreateObjectContract (objectType);
        }

        protected override IValueProvider CreateMemberValueProvider(MemberInfo member)
        {
            if (member.MemberType == System.Reflection.MemberTypes.Property && ((PropertyInfo)member).PropertyType.IsEnum)
                return new EnumValueProvider(member);
            else if (member.MemberType == System.Reflection.MemberTypes.Field && ((FieldInfo)member).FieldType.IsEnum)
                return new EnumValueProvider(member);
            else
                return base.CreateMemberValueProvider(member);
        }

        /// <summary>
        /// Get and set values for a <see cref="MemberInfo"/> Enum using reflection.
        /// </summary>
        private class EnumValueProvider : IValueProvider
        {
            private Type m_enumType;

            private MemberInfo m_member;

            /// <summary>
            /// Initializes a new instance of the <see cref="EnumValueProvider"/> class.
            /// </summary>
            /// <param name="pi">The property info.</param>
            public EnumValueProvider(MemberInfo mi)
            {
                if (mi == null)
                    throw new ArgumentNullException("mi");
                if (mi.MemberType == MemberTypes.Property)
                    m_enumType = ((PropertyInfo)mi).PropertyType;
                else if (mi.MemberType == MemberTypes.Field)
                    m_enumType = ((FieldInfo)mi).FieldType;
                else
                    throw new ArgumentException(string.Format("MemberInfo '{0}' must be of type FieldInfo or PropertyInfo", CultureInfo.InvariantCulture, m_member.Name, "member"));

                m_member = mi;
            }

            /// <summary>
            /// Sets the value.
            /// </summary>
            /// <param name="target">The target to set the value on.</param>
            /// <param name="value">The value to set on the target.</param>
            public void SetValue(object target, object value)
            {
                try
                {
                    if (target == null)
                        throw new ArgumentNullException("target");

                    object parsedValue = value is string ? Enum.Parse(m_enumType, value as String, true) : value;

                    switch (m_member.MemberType)
                    {
                        case MemberTypes.Field:
                            ((FieldInfo)m_member).SetValue(target, parsedValue);
                            break;
                        case MemberTypes.Property:
                            ((PropertyInfo)m_member).SetValue(target, parsedValue, null);
                            break;
                        default:
                            throw new ArgumentException(string.Format("MemberInfo '{0}' must be of type FieldInfo or PropertyInfo", CultureInfo.InvariantCulture, m_member.Name, "member"));
                    }
                }
                catch (Exception ex)
                {
                    throw new JsonSerializationException(string.Format("Error setting value to '{0}' on '{1}'.", CultureInfo.InvariantCulture, m_member.Name, (target == null ? "null" : target.GetType().FullName)), ex);
                }
            }

            /// <summary>
            /// Gets the value.
            /// </summary>
            /// <param name="target">The target to get the value from.</param>
            /// <returns>The value.</returns>
            public object GetValue(object target)
            {
                try
                {
                    if (target == null)
                        throw new ArgumentNullException("target");

                    switch (m_member.MemberType)
                    {
                        case MemberTypes.Field:
                            return ((FieldInfo)m_member).GetValue(target).ToString();
                        case MemberTypes.Property:
                            return ((PropertyInfo)m_member).GetValue(target, null).ToString();
                        default:
                            throw new ArgumentException(string.Format("MemberInfo '{0}' must be of type FieldInfo or PropertyInfo", CultureInfo.InvariantCulture, m_member.Name, "member"));
                    }
                }
                catch (Exception ex)
                {
                    throw new JsonSerializationException(string.Format("Error getting value to '{0}' on '{1}'.", CultureInfo.InvariantCulture, m_member.Name, (target == null ? "null" : target.GetType().FullName)), ex);
                }
            }
        }
	}
}

