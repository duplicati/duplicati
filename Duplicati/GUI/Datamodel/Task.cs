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
// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Dokumenter\duplicati\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>Task</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("Task")]
	public partial class Task : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, Relation("LogTask", typeof(Log), "TaskID", false), Relation("TaskFilterTask", typeof(TaskFilter), "TaskID", false), Relation("BackendSettingTask", typeof(BackendSetting), "TaskID", false), Relation("TaskExtensionTask", typeof(TaskExtension), "TaskID", false), Relation("TaskOverrideTask", typeof(TaskOverride), "TaskID", false), Relation("CompressionSettingTask", typeof(CompressionSetting), "TaskID", false), Relation("EncryptionSettingTask", typeof(EncryptionSetting), "TaskID", false), DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("Service")]
		private System.String m_Service = "";
		[DatabaseField("Encryptionkey")]
		private System.String m_Encryptionkey = "";
		[DatabaseField("SourcePath")]
		private System.String m_SourcePath = "";
		[Relation("TaskSchedule", typeof(Schedule), "ID"), DatabaseField("ScheduleID")]
		private System.Int64 m_ScheduleID = long.MinValue;
		[DatabaseField("KeepFull")]
		private System.Int64 m_KeepFull = long.MinValue;
		[DatabaseField("KeepTime")]
		private System.String m_KeepTime = "";
		[DatabaseField("FullAfter")]
		private System.String m_FullAfter = "";
		[DatabaseField("IncludeSetup")]
		private System.Boolean m_IncludeSetup = false;
		[DatabaseField("EncryptionModule")]
		private System.String m_EncryptionModule = "";
		[DatabaseField("CompressionModule")]
		private System.String m_CompressionModule = "";
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

		public System.Int64 KeepFull
		{
			get{return m_KeepFull;}
			set{object oldvalue = m_KeepFull;OnBeforeDataChange(this, "KeepFull", oldvalue, value);m_KeepFull = value;OnAfterDataChange(this, "KeepFull", oldvalue, value);}
		}

		public System.String KeepTime
		{
			get{return m_KeepTime;}
			set{object oldvalue = m_KeepTime;OnBeforeDataChange(this, "KeepTime", oldvalue, value);m_KeepTime = value;OnAfterDataChange(this, "KeepTime", oldvalue, value);}
		}

		public System.String FullAfter
		{
			get{return m_FullAfter;}
			set{object oldvalue = m_FullAfter;OnBeforeDataChange(this, "FullAfter", oldvalue, value);m_FullAfter = value;OnAfterDataChange(this, "FullAfter", oldvalue, value);}
		}

		public System.Boolean IncludeSetup
		{
			get{return m_IncludeSetup;}
			set{object oldvalue = m_IncludeSetup;OnBeforeDataChange(this, "IncludeSetup", oldvalue, value);m_IncludeSetup = value;OnAfterDataChange(this, "IncludeSetup", oldvalue, value);}
		}

		public System.String EncryptionModule
		{
			get{return m_EncryptionModule;}
			set{object oldvalue = m_EncryptionModule;OnBeforeDataChange(this, "EncryptionModule", oldvalue, value);m_EncryptionModule = value;OnAfterDataChange(this, "EncryptionModule", oldvalue, value);}
		}

		public System.String CompressionModule
		{
			get{return m_CompressionModule;}
			set{object oldvalue = m_CompressionModule;OnBeforeDataChange(this, "CompressionModule", oldvalue, value);m_CompressionModule = value;OnAfterDataChange(this, "CompressionModule", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Schedule))]
		public Schedule Schedule
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Schedule>("TaskSchedule", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("TaskSchedule", this, value); }
		}

		[Affects(typeof(Log))]
		public System.Collections.Generic.IList<Log> Logs
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<Log>("LogTask", this);
			}
		}

		[Affects(typeof(TaskFilter))]
		public System.Collections.Generic.IList<TaskFilter> Filters
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<TaskFilter>("TaskFilterTask", this);
			}
		}

		[Affects(typeof(BackendSetting))]
		public System.Collections.Generic.IList<BackendSetting> BackendSettings
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<BackendSetting>("BackendSettingTask", this);
			}
		}

		[Affects(typeof(TaskExtension))]
		public System.Collections.Generic.IList<TaskExtension> TaskExtensions
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<TaskExtension>("TaskExtensionTask", this);
			}
		}

		[Affects(typeof(TaskOverride))]
		public System.Collections.Generic.IList<TaskOverride> TaskOverrides
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<TaskOverride>("TaskOverrideTask", this);
			}
		}

		[Affects(typeof(CompressionSetting))]
		public System.Collections.Generic.IList<CompressionSetting> CompressionSettings
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<CompressionSetting>("CompressionSettingTask", this);
			}
		}

		[Affects(typeof(EncryptionSetting))]
		public System.Collections.Generic.IList<EncryptionSetting> EncryptionSettings
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<EncryptionSetting>("EncryptionSettingTask", this);
			}
		}

#endregion

	}

}