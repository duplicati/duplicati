/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Dokumenter\duplicati\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>CommandQueue</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("CommandQueue")]
	public partial class CommandQueue : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("Command")]
		private System.String m_Command = "";
		[DatabaseField("Argument")]
		private System.String m_Argument = "";
		[DatabaseField("Completed")]
		private System.Boolean m_Completed = false;
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.String Command
		{
			get{return m_Command;}
			set{object oldvalue = m_Command;OnBeforeDataChange(this, "Command", oldvalue, value);m_Command = value;OnAfterDataChange(this, "Command", oldvalue, value);}
		}

		public System.String Argument
		{
			get{return m_Argument;}
			set{object oldvalue = m_Argument;OnBeforeDataChange(this, "Argument", oldvalue, value);m_Argument = value;OnAfterDataChange(this, "Argument", oldvalue, value);}
		}

		public System.Boolean Completed
		{
			get{return m_Completed;}
			set{object oldvalue = m_Completed;OnBeforeDataChange(this, "Completed", oldvalue, value);m_Completed = value;OnAfterDataChange(this, "Completed", oldvalue, value);}
		}

#endregion

#region " referenced properties "

#endregion

	}

}