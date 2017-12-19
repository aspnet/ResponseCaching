using System.Diagnostics;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public static class ResponseCachingDiagnosticSourceExtensions
    {
        private const string BeforeTryServeFromCacheName =
            "Microsoft.AspNetCore.ResponseCaching.BeforeTryServeFromCache";
        private const string AfterTryServeFromCacheName =
            "Microsoft.AspNetCore.ResponseCaching.AfterTryServeFromCache";
        private const string BeforeCacheResponseName =
            "Microsoft.AspNetCore.ResponseCaching.BeforeCacheResponse";
        private const string AfterCacheResponseName =
            "Microsoft.AspNetCore.ResponseCaching.AfterCacheResponse";

        public static void BeforeTryServeFromCache(this DiagnosticSource diagnosticSource,
            ResponseCachingContext responseCachingContext)
        {
            Debug.Assert(diagnosticSource != null);
            Debug.Assert(responseCachingContext != null);

            if (diagnosticSource.IsEnabled(BeforeTryServeFromCacheName))
            {
                diagnosticSource.Write(BeforeTryServeFromCacheName, new { responseCachingContext });
            }
        }

        public static void AfterTryServeFromCache(this DiagnosticSource diagnosticSource,
            ResponseCachingContext responseCachingContext, bool servedFromCache)
        {
            Debug.Assert(diagnosticSource != null);
            Debug.Assert(responseCachingContext != null);

            if (diagnosticSource.IsEnabled(AfterTryServeFromCacheName))
            {
                diagnosticSource.Write(AfterTryServeFromCacheName, new { responseCachingContext, servedFromCache });
            }
        }

        public static void BeforeCacheResponse(this DiagnosticSource diagnosticSource,
            ResponseCachingContext responseCachingContext)
        {
            Debug.Assert(diagnosticSource != null);
            Debug.Assert(responseCachingContext != null);

            if (diagnosticSource.IsEnabled(BeforeCacheResponseName))
            {
                diagnosticSource.Write(BeforeCacheResponseName, new { responseCachingContext });
            }
        }

        public static void AfterCacheResponse(this DiagnosticSource diagnosticSource,
            ResponseCachingContext responseCachingContext)
        {
            Debug.Assert(diagnosticSource != null);
            Debug.Assert(responseCachingContext != null);

            if (diagnosticSource.IsEnabled(AfterCacheResponseName))
            {
                diagnosticSource.Write(AfterCacheResponseName, new { responseCachingContext });
            }
        }
    }
}