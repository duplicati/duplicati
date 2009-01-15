/// <metadata>
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

		[AutoIncrement, PrimaryKey, Relation("TaskSettingTask", typeof(TaskSetting), "TaskID", false), Relation("LogTask", typeof(Log), "TaskID", false), DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("Service")]
		private System.String m_Service = "";
		[DatabaseField("Encryptionkey")]
		private System.String m_Encryptionkey = "";
		[DatabaseField("Signaturekey")]
		private System.String m_Signaturekey = "";
		[DatabaseField("SourcePath")]
		private System.String m_SourcePath = "";
		[Relation("TaskSchedule", typeof(Schedule), "ID"), DatabaseField("ScheduleID")]
		private System.Int64 m_ScheduleID = long.MinValue;
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

		[Affects(typeof(Schedule))]
		public Schedule Schedule
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Schedule>("TaskSchedule", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("TaskSchedule", this, value); }
		}

		[Affects(typeof(TaskSetting))]
		public System.Collections.Generic.IList<TaskSetting> TaskSettings
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<TaskSetting>("TaskSettingTask", this);
			}
		}

		[Affects(typeof(Log))]
		public System.Collections.Generic.IList<Log> Logs
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<Log>("LogTask", this);
			}
		}

#endregion

	}

}