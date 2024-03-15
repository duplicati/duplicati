namespace Duplicati.WebserverCore.Database.Entities;

public class Option
{
    public int BackupID { get; set; }
    public string Filter { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
}