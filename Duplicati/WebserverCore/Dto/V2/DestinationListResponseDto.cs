namespace Duplicati.WebserverCore.Dto.V2;

/// <summary>
/// A single item in the destination list response
/// </summary>
public sealed record DestinationListResponseItem
{
    /// <summary>
    /// The item path
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The item size
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Any metadata associated with the item
    /// </summary>
    public required IDictionary<string, string?>? Metadata { get; init; }
}

/// <summary>
/// The content of the destination list response
/// </summary>
public sealed record DestinationListResponesContent
{
    /// <summary>
    /// The items in the response
    /// </summary>
    public required IEnumerable<DestinationListResponseItem> Items { get; init; }
    /// <summary>
    /// The offset of the first item in the response
    /// </summary>
    public required int Offset { get; init; }
    /// <summary>
    /// Whether there are more items to return
    /// </summary>
    public required bool HasMore { get; init; }
}

/// <summary>
/// The response from the destination list endpoint
/// </summary>
public sealed record DestinationListResponseDto : ResponseEnvelope<DestinationListResponesContent>
{
    /// <summary>
    /// Creates a failure response
    /// </summary>
    /// <param name="error">The error message</param>
    /// <returns>The response</returns>
    public static DestinationListResponseDto Failure(string error)
        => new DestinationListResponseDto()
        {
            Error = error,
            StatusCode = "Failed",
            Success = false,
            Data = null
        };

    /// <summary>
    /// Creates a success response
    /// </summary>
    /// <param name="items">The items to return</param>
    /// <param name="offset">The offset of the first item</param>
    /// <param name="hasMore">Whether there are more items to return</param>
    /// <returns>The response</returns>
    public static DestinationListResponseDto Create(IEnumerable<DestinationListResponseItem> items, int offset, bool hasMore)
        => new DestinationListResponseDto()
        {
            Error = null,
            StatusCode = "Success",
            Success = true,
            Data = new DestinationListResponesContent()
            {
                Items = items,
                Offset = offset,
                HasMore = hasMore
            }
        };

}
