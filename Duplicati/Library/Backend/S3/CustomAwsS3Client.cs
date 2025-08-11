using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.S3;

/// <summary>
/// Custom AWS S3 client that overrides the default behavior to lowercase the "x-amz-content-sha256" header.
/// This is necessary for compatibility with certain S3 implementations that expect this header to be in lowercase.
/// </summary>
public class CustomAwsS3Client : AmazonS3Client
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAwsS3Client"/> class with the specified credentials and configuration.
    /// </summary>
    /// <param name="credentials">The AWS credentials to use for authentication.</param>
    /// <param name="config">>The AWS S3 configuration to use.</param>
    public CustomAwsS3Client(string awsId, string awsKey, AmazonS3Config config)
        : base(awsId, awsKey, config)
    {
    }

    /// <inheritdoc />
    protected override void CustomizeRuntimePipeline(RuntimePipeline pipeline)
    {
        base.CustomizeRuntimePipeline(pipeline);

        pipeline.AddHandlerAfter<Signer>(
            new LowercaseSha256HeaderHandler());
    }

    /// <summary>
    /// Handler that lowercases the "x-amz-content-sha256" header in the request.
    /// </summary>
    private class LowercaseSha256HeaderHandler : PipelineHandler
    {
        /// <inheritdoc />
        public override void InvokeSync(IExecutionContext executionContext)
        {
            LowercaseHeader(executionContext.RequestContext.Request);
            base.InvokeSync(executionContext);
        }

        /// <inheritdoc />
        public override async Task<T> InvokeAsync<T>(IExecutionContext executionContext)
        {
            LowercaseHeader(executionContext.RequestContext.Request);
            return await base.InvokeAsync<T>(executionContext).ConfigureAwait(false);
        }

        /// <summary>
        /// Lowercases the "x-amz-content-sha256" header in the request if it exists.
        /// </summary>
        /// <param name="request">The request to modify.</param>
        private void LowercaseHeader(IRequest request)
        {
            const string headerName = "x-amz-content-sha256";
            if (request.Headers.TryGetValue(headerName, out var value) && !string.IsNullOrEmpty(value))
            {
                request.Headers[headerName] = value.ToLowerInvariant();
            }
        }
    }
}