/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=C:\Documents and Settings\Kenneth\Dokumenter\duplicati\Duplicati\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>TaskSetting</name>
/// <sql></sql>
/// </metadata>

namespace Duplicati.Datamodel
{

	public partial class TaskSetting : System.Data.LightDatamodel.DataClassBase
	{

#region " private members "

		[System.Data.LightDatamodel.MemberModifierAutoIncrement()]
		private System.Int64 m_ID = 0;
		private System.Int64 m_TaskID = -9223372036854775808;
		private System.String m_Name = "";
		private System.String m_Value = "";
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

		public Task Task
		{
			get{ return base.RelationManager.GetReferenceObject<Task>("Task", this); }
			set{ base.RelationManager.SetReferenceObject<Task>("Task", this, value); }
		}

#endregion

	}

}