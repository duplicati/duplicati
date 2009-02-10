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

		[AutoIncrement, PrimaryKey, Relation("TaskSettingTask", typeof(TaskSetting), "TaskID", false), Relation("LogTask", typeof(Log), "TaskID", false), Relation("TaskFilterTask", typeof(TaskFilter), "TaskID", false), DatabaseField("ID")]
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
		[DatabaseField("KeepFull")]
		private System.Int64 m_KeepFull = long.MinValue;
		[DatabaseField("KeepTime")]
		private System.String m_KeepTime = "";
		[DatabaseField("MaxUploadsize")]
		private System.String m_MaxUploadsize = "";
		[DatabaseField("UploadBandwidth")]
		private System.String m_UploadBandwidth = "";
		[DatabaseField("DownloadBandwidth")]
		private System.String m_DownloadBandwidth = "";
		[DatabaseField("VolumeSize")]
		private System.String m_VolumeSize = "";
		[DatabaseField("FullAfter")]
		private System.String m_FullAfter = "";
		[DatabaseField("ThreadPriority")]
		private System.String m_ThreadPriority = "";
		[DatabaseField("AsyncTransfer")]
		private System.Boolean m_AsyncTransfer = false;
		[DatabaseField("GPGEncryption")]
		private System.Boolean m_GPGEncryption = false;
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

		public System.String MaxUploadsize
		{
			get{return m_MaxUploadsize;}
			set{object oldvalue = m_MaxUploadsize;OnBeforeDataChange(this, "MaxUploadsize", oldvalue, value);m_MaxUploadsize = value;OnAfterDataChange(this, "MaxUploadsize", oldvalue, value);}
		}

		public System.String UploadBandwidth
		{
			get{return m_UploadBandwidth;}
			set{object oldvalue = m_UploadBandwidth;OnBeforeDataChange(this, "UploadBandwidth", oldvalue, value);m_UploadBandwidth = value;OnAfterDataChange(this, "UploadBandwidth", oldvalue, value);}
		}

		public System.String DownloadBandwidth
		{
			get{return m_DownloadBandwidth;}
			set{object oldvalue = m_DownloadBandwidth;OnBeforeDataChange(this, "DownloadBandwidth", oldvalue, value);m_DownloadBandwidth = value;OnAfterDataChange(this, "DownloadBandwidth", oldvalue, value);}
		}

		public System.String VolumeSize
		{
			get{return m_VolumeSize;}
			set{object oldvalue = m_VolumeSize;OnBeforeDataChange(this, "VolumeSize", oldvalue, value);m_VolumeSize = value;OnAfterDataChange(this, "VolumeSize", oldvalue, value);}
		}

		public System.String FullAfter
		{
			get{return m_FullAfter;}
			set{object oldvalue = m_FullAfter;OnBeforeDataChange(this, "FullAfter", oldvalue, value);m_FullAfter = value;OnAfterDataChange(this, "FullAfter", oldvalue, value);}
		}

		public System.String ThreadPriority
		{
			get{return m_ThreadPriority;}
			set{object oldvalue = m_ThreadPriority;OnBeforeDataChange(this, "ThreadPriority", oldvalue, value);m_ThreadPriority = value;OnAfterDataChange(this, "ThreadPriority", oldvalue, value);}
		}

		public System.Boolean AsyncTransfer
		{
			get{return m_AsyncTransfer;}
			set{object oldvalue = m_AsyncTransfer;OnBeforeDataChange(this, "AsyncTransfer", oldvalue, value);m_AsyncTransfer = value;OnAfterDataChange(this, "AsyncTransfer", oldvalue, value);}
		}

		public System.Boolean GPGEncryption
		{
			get{return m_GPGEncryption;}
			set{object oldvalue = m_GPGEncryption;OnBeforeDataChange(this, "GPGEncryption", oldvalue, value);m_GPGEncryption = value;OnAfterDataChange(this, "GPGEncryption", oldvalue, value);}
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

		[Affects(typeof(TaskFilter))]
		public System.Collections.Generic.IList<TaskFilter> Filters
		{
			get
			{
				return ((DataFetcherWithRelations)m_dataparent).GetRelatedObjects<TaskFilter>("TaskFilterTask", this);
			}
		}

#endregion

	}

}