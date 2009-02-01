/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Dokumenter\duplicati\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>TaskFilter</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("TaskFilter")]
	public partial class TaskFilter : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("SortOrder")]
		private System.Int64 m_SortOrder = long.MinValue;
		[DatabaseField("Include")]
		private System.Boolean m_Include = false;
		[DatabaseField("Filter")]
		private System.String m_Filter = "";
		[Relation("TaskFilterTask", typeof(Task), "ID"), DatabaseField("TaskID")]
		private System.Int64 m_TaskID = long.MinValue;
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.Int64 SortOrder
		{
			get{return m_SortOrder;}
			set{object oldvalue = m_SortOrder;OnBeforeDataChange(this, "SortOrder", oldvalue, value);m_SortOrder = value;OnAfterDataChange(this, "SortOrder", oldvalue, value);}
		}

		public System.Boolean Include
		{
			get{return m_Include;}
			set{object oldvalue = m_Include;OnBeforeDataChange(this, "Include", oldvalue, value);m_Include = value;OnAfterDataChange(this, "Include", oldvalue, value);}
		}

		public System.String Filter
		{
			get{return m_Filter;}
			set{object oldvalue = m_Filter;OnBeforeDataChange(this, "Filter", oldvalue, value);m_Filter = value;OnAfterDataChange(this, "Filter", oldvalue, value);}
		}

		public System.Int64 TaskID
		{
			get{return m_TaskID;}
			set{object oldvalue = m_TaskID;OnBeforeDataChange(this, "TaskID", oldvalue, value);m_TaskID = value;OnAfterDataChange(this, "TaskID", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Task))]
		public Task Task
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Task>("TaskFilterTask", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("TaskFilterTask", this, value); }
		}

#endregion

	}

}