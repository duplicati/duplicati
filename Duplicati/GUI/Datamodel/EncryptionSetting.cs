#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Documents\duplicati\gpg-commandline-settings\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>EncryptionSetting</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("EncryptionSetting")]
	public partial class EncryptionSetting : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[Relation("EncryptionSettingTask", typeof(Task), "ID"), DatabaseField("TaskID")]
		private System.Int64 m_TaskID = long.MinValue;
		[DatabaseField("Name")]
		private System.String m_Name = "";
		[DatabaseField("Value")]
		private System.String m_Value = "";
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.Int64 TaskID
		{
			get{return m_TaskID;}
			set{object oldvalue = m_TaskID;OnBeforeDataChange(this, "TaskID", oldvalue, value);m_TaskID = value;OnAfterDataChange(this, "TaskID", oldvalue, value);}
		}

		public System.String Name
		{
			get{return m_Name;}
			set{object oldvalue = m_Name;OnBeforeDataChange(this, "Name", oldvalue, value);m_Name = value;OnAfterDataChange(this, "Name", oldvalue, value);}
		}

		public System.String Value
		{
			get{return m_Value;}
			set{object oldvalue = m_Value;OnBeforeDataChange(this, "Value", oldvalue, value);m_Value = value;OnAfterDataChange(this, "Value", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Task))]
		public Task Task
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Task>("EncryptionSettingTask", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("EncryptionSettingTask", this, value); }
		}

#endregion

	}

}