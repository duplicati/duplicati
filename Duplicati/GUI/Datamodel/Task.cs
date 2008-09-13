#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=C:\Documents and Settings\Kenneth\Dokumenter\duplicati\Duplicati\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>Task</name>
/// <sql></sql>
/// </metadata>

namespace Duplicati.Datamodel
{

	public partial class Task : System.Data.LightDatamodel.DataClassBase
	{

#region " private members "

		[System.Data.LightDatamodel.MemberModifierAutoIncrement()]
		private System.Int64 m_ID = 0;
		private System.String m_Service = "";
		private System.String m_Encryptionkey = "";
		private System.String m_Signaturekey = "";
		private System.String m_SourcePath = "";
		private System.Int64 m_ScheduleID = -9223372036854775808;
#endregion

#region " unique value "

		public override object UniqueValue {get{return m_ID;}}
		public override string UniqueColumn {get{return "ID";}}
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.String Service
		{
			get{return m_Service;}
			set{object oldvalue = m_Service;OnBeforeDataChange(this, "Service", oldvalue, value);m_Service = value;OnAfterDataChange(this, "Service", oldvalue, value);}
		}

		public System.String Encryptionkey
		{
			get{return m_Encryptionkey;}
			set{object oldvalue = m_Encryptionkey;OnBeforeDataChange(this, "Encryptionkey", oldvalue, value);m_Encryptionkey = value;OnAfterDataChange(this, "Encryptionkey", oldvalue, value);}
		}

		public System.String Signaturekey
		{
			get{return m_Signaturekey;}
			set{object oldvalue = m_Signaturekey;OnBeforeDataChange(this, "Signaturekey", oldvalue, value);m_Signaturekey = value;OnAfterDataChange(this, "Signaturekey", oldvalue, value);}
		}

		public System.String SourcePath
		{
			get{return m_SourcePath;}
			set{object oldvalue = m_SourcePath;OnBeforeDataChange(this, "SourcePath", oldvalue, value);m_SourcePath = value;OnAfterDataChange(this, "SourcePath", oldvalue, value);}
		}

		public System.Int64 ScheduleID
		{
			get{return m_ScheduleID;}
			set{object oldvalue = m_ScheduleID;OnBeforeDataChange(this, "ScheduleID", oldvalue, value);m_ScheduleID = value;OnAfterDataChange(this, "ScheduleID", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		public Schedule Schedule
		{
			get{ return base.RelationManager.GetReferenceObject<Schedule>("Schedule", this); }
			set{ base.RelationManager.SetReferenceObject<Schedule>("Schedule", this, value); }
		}

		private System.Collections.Generic.IList<TaskSetting> m_TaskSettings;
		public System.Collections.Generic.IList<TaskSetting> TaskSettings
		{
			get
			{
				if (m_TaskSettings == null)
					m_TaskSettings = base.RelationManager.GetReferenceCollection<TaskSetting>("Task", this);
				return m_TaskSettings;
			}
		}

		private System.Collections.Generic.IList<Log> m_Logs;
		public System.Collections.Generic.IList<Log> Logs
		{
			get
			{
				if (m_Logs == null)
					m_Logs = base.RelationManager.GetReferenceCollection<Log>("OwnerTask", this);
				return m_Logs;
			}
		}

#endregion

	}

}