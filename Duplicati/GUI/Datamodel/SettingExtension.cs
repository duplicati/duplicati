/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Documents\duplicati\gpg-commandline-settings\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>SettingExtension</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("SettingExtension")]
	public partial class SettingExtension : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("SettingKey")]
		private System.String m_SettingKey = "";
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

		public System.String SettingKey
		{
			get{return m_SettingKey;}
			set{object oldvalue = m_SettingKey;OnBeforeDataChange(this, "SettingKey", oldvalue, value);m_SettingKey = value;OnAfterDataChange(this, "SettingKey", oldvalue, value);}
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

#endregion

	}

}