/************************************************************************
 * Options for configuring an IResponseHandler implementation.
 ************************************************************************/
namespace JamaaTech.Smpp.Net.Lib
{
    /// <summary>
    /// Selects an <see cref="IResponseHandler"/> implementation.
    /// </summary>
    public enum ResponseHandlerImplementation
    {
        /// <summary>The default TaskCompletionSource-based handler.</summary>
        Default = 0,

        /// <summary>A handler tuned for many in-flight requests on one session.</summary>
        Concurrent = 1
    }

    /// <summary>
    /// Options to configure a response handler implementation.
    /// </summary>
    public class ResponseHandlerOptions
    {
        /// <summary>
        /// Default timeout in milliseconds (minimum enforced = 5000).
        /// </summary>
        public int DefaultResponseTimeout { get; set; } = 5000;

        /// <summary>
        /// The implementation to create. Defaults to
        /// <see cref="ResponseHandlerImplementation.Default"/>.
        /// </summary>
        public ResponseHandlerImplementation Implementation { get; set; } = ResponseHandlerImplementation.Default;
    }
}
