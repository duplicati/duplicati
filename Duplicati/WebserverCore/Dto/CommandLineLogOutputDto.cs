namespace Duplicati.WebserverCore.Dto;
/// <summary>
/// The command line log output DTO
/// </summary>
/// <param name="Pagesize">The page size</param>
/// <param name="Offset">The offset</param>
/// <param name="Count">The count</param>
/// <param name="Items">The items</param>
/// <param name="Finished">Whether the command line log is finished</param>
/// <param name="Started">Whether the command line log is started</param>
public record CommandLineLogOutputDto(
    int Pagesize,
    int Offset,
    int Count,
    IEnumerable<string> Items,
    bool Finished,
    bool Started
);