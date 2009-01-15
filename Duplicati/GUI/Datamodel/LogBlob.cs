/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Dokumenter\duplicati\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>LogBlob</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("LogBlob")]
	public partial class LogBlob : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, Relation("LogBlob", typeof(Log), "LogBlobID", false), DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("Data")]
		private System.Byte[] m_Data = null;
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.Byte[] Data
		{
			get{return m_Data;}
			set{object oldvalue = m_Data;OnBeforeDataChange(this, "Data", oldvalue, value);m_Data = value;OnAfterDataChange(this, "Data", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Log))]
		public Log Log
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Log>("LogBlob", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("LogBlob", this, value); }
		}

#endregion

	}

}