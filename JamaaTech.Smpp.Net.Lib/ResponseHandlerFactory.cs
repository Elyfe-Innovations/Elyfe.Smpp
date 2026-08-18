/************************************************************************
 * Factory for creating IResponseHandler based on ResponseHandlerOptions.
 ************************************************************************/

using Microsoft.Extensions.Options;

namespace JamaaTech.Smpp.Net.Lib
{
    public static class ResponseHandlerFactory
    {
        private static IOptions<ResponseHandlerOptions> _options = Options.Create(new ResponseHandlerOptions());

        /// <summary>
        /// Creates a handler from the ambient options set by <see cref="Configure"/>.
        /// </summary>
        public static IResponseHandler Create()
        {
            return Create(_options.Value);
        }

        /// <summary>
        /// Creates a handler from <paramref name="options"/>, or from the defaults when it
        /// is <see langword="null"/>.
        /// </summary>
        public static IResponseHandler Create(ResponseHandlerOptions options)
        {
            options ??= new ResponseHandlerOptions();

            switch (options.Implementation)
            {
                case ResponseHandlerImplementation.Concurrent:
                    return new ConcurrentResponseHandler(options);

                case ResponseHandlerImplementation.Default:
                default:
                    return new ResponseHandlerV2 { DefaultResponseTimeout = options.DefaultResponseTimeout };
            }
        }

        /// <summary>
        /// Sets the options <see cref="Create()"/> reads. Callers that already have a DI
        /// container should resolve <see cref="IOptions{TOptions}"/> and pass it here once
        /// during startup; the last call wins.
        /// </summary>
        public static void Configure(IOptions<ResponseHandlerOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc cref="Configure(IOptions{ResponseHandlerOptions})"/>
        public static void Configure(ResponseHandlerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            Configure(Options.Create(options));
        }
    }
}
