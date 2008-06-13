/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=C:\Documents and Settings\Kenneth\Dokumenter\duplicati\Duplicati\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>Schedule</name>
/// <sql></sql>
/// </metadata>

namespace Duplicati.Datamodel
{

	public partial class Schedule : System.Data.LightDatamodel.DataClassBase
	{

#region " private members "

		[System.Data.LightDatamodel.MemberModifierAutoIncrement()]
		private System.Int64 m_ID = 0;
		private System.String m_Name = "";
		private System.String m_Path = "";
		private System.DateTime m_When = new System.DateTime(1, 1, 1);
		private System.String m_Repeat = "";
		private System.String m_Weekdays = "";
		private System.Int64 m_KeepFull = 0;
		private System.String m_KeepTime = "";
		private System.String m_FullAfter = "";
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

		public System.String Name
		{
			get{return m_Name;}
			set{object oldvalue = m_Name;OnBeforeDataChange(this, "Name", oldvalue, value);m_Name = value;OnAfterDataChange(this, "Name", oldvalue, value);}
		}

		public System.String Path
		{
			get{return m_Path;}
			set{object oldvalue = m_Path;OnBeforeDataChange(this, "Path", oldvalue, value);m_Path = value;OnAfterDataChange(this, "Path", oldvalue, value);}
		}

		public System.DateTime When
		{
			get{return m_When;}
			set{object oldvalue = m_When;OnBeforeDataChange(this, "When", oldvalue, value);m_When = value;OnAfterDataChange(this, "When", oldvalue, value);}
		}

		public System.String Repeat
		{
			get{return m_Repeat;}
			set{object oldvalue = m_Repeat;OnBeforeDataChange(this, "Repeat", oldvalue, value);m_Repeat = value;OnAfterDataChange(this, "Repeat", oldvalue, value);}
		}

		public System.String Weekdays
		{
			get{return m_Weekdays;}
			set{object oldvalue = m_Weekdays;OnBeforeDataChange(this, "Weekdays", oldvalue, value);m_Weekdays = value;OnAfterDataChange(this, "Weekdays", oldvalue, value);}
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

#endregion

#region " referenced properties "

		private System.Collections.Generic.IList<Task> m_Tasks;
		public System.Collections.Generic.IList<Task> Tasks
		{
			get
			{
				if (m_Tasks == null)
					m_Tasks = base.RelationManager.GetReferenceCollection<Task>("Schedule", this);
				return m_Tasks;
			}
		}

#endregion

	}

}