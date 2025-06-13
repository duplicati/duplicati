namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// The base response envelope
/// </summary>
public record ResponseEnvelope
{
    /// <summary>
    /// The status of the response
    /// </summary>
    public required bool Success { get; set; }
    /// <summary>
    /// The error message, if any
    /// </summary>
    public required string? Error { get; set; }
    /// <summary>
    /// The status code, if any
    /// </summary>
    public required string StatusCode { get; set; }

    /// <summary>
    /// An empty response envelope
    /// </summary>
    public static readonly ResponseEnvelope Empty = new()
    {
        Success = true,
        Error = null,
        StatusCode = "OK"
    };

    /// <summary>
    /// Creates an error response envelope
    /// </summary>
    /// <param name="message">The human-readable error message</param>
    /// <param name="errorCode">The status code error message</param>
    /// <returns>The response envelop</returns>
    public static ResponseEnvelope Failure(string message, string errorCode)
        => new ResponseEnvelope
        {
            Success = false,
            Error = message,
            StatusCode = errorCode
        };

    /// <summary>
    /// Creates a success response envelope
    /// </summary>
    /// <typeparam name="T">The type of the data</typeparam>
    /// <param name="data">The data</param>
    /// <returns>The response envelope</returns>
    public static ResponseEnvelope<T> Result<T>(T? data)
        => new ResponseEnvelope<T>
        {
            Success = true,
            Data = data,
            StatusCode = "OK",
            Error = null
        };

    /// <summary>
    /// Creates a success response envelope with paging information
    /// </summary>
    /// <typeparam name="T">The type of the data</typeparam>
    /// <param name="data">The data result</param>
    /// <param name="total">The total number of items</param>
    /// <param name="page">The current page</param>
    /// <param name="pages">The total number of pages</param>
    /// <returns>The response envelope</returns>
    public static PagedResponseEnvelope<T> PageResult<T>(IEnumerable<T> data, int total, int pageSize, int page, int pages)
        => new PagedResponseEnvelope<T>
        {
            Success = true,
            Data = data,
            PageInfo = new PageInfo
            {
                Total = total,
                Page = page,
                Pages = pages,
                PageSize = pageSize
            },
            StatusCode = "OK",
            Error = null
        };
}

/// <summary>
/// The response envelope with data
/// </summary>
/// <typeparam name="T">The type of the data</typeparam>
public record ResponseEnvelope<T> : ResponseEnvelope
{
    public T? Data { get; set; }
}

/// <summary>
/// The response envelope with paging information
/// </summary>
/// <typeparam name="T">The type of the data</typeparam>
public record PagedResponseEnvelope<T> : ResponseEnvelope<IEnumerable<T>>
{
    /// <summary>
    /// The pagination information
    /// </summary>
    public required PageInfo? PageInfo { get; init; }
}

/// <summary>
/// Pagination information
/// </summary>
public record PageInfo
{
    /// <summary>
    /// The current page
    /// </summary>
    public int Page { get; set; }
    /// <summary>
    /// The page size
    /// </summary>
    public int PageSize { get; set; }
    /// <summary>
    /// The total number of items
    /// </summary>
    public long Total { get; set; }
    /// <summary>
    /// The total number of pages
    /// </summary>
    public int Pages { get; set; }
}

/// <summary>
/// The fileset DTO
/// </summary>
public abstract record PaginatedRequest
{
    /// <summary>
    /// The page size
    /// </summary>
    public required int? PageSize { get; init; } = 1000;

    /// <summary>
    /// The page number
    /// </summary>
    public required int? Page { get; init; }
}