using System.Diagnostics;
using Microsoft.Extensions.DiagnosticAdapter;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class TestDiagnosticListener
    {
        public class BeforeTryServeFromCacheData
        {
            public IProxyResponseCachingContext ResponseCachingContext { get; set; }
        }

        public BeforeTryServeFromCacheData BeforeTryServeFromCache { get; set; }

        [DiagnosticName("Microsoft.AspNetCore.ResponseCaching.BeforeTryServeFromCache")]
        public virtual void OnBeforeTryServeFromCache(IProxyResponseCachingContext responseCachingContext)
        {
            BeforeTryServeFromCache = new BeforeTryServeFromCacheData
            {
                ResponseCachingContext = responseCachingContext
            };
        }

        public class AfterTryServeFromCacheData
        {
            public IProxyResponseCachingContext ResponseCachingContext { get; set; }
            public bool ServedFromCache { get; set; }
        }

        public AfterTryServeFromCacheData AfterTryServeFromCache { get; set; }

        [DiagnosticName("Microsoft.AspNetCore.ResponseCaching.AfterTryServeFromCache")]
        public virtual void OnAfterTryServeFromCache(IProxyResponseCachingContext responseCachingContext, bool servedFromCache)
        {
            AfterTryServeFromCache = new AfterTryServeFromCacheData
            {
                ResponseCachingContext = responseCachingContext,
                ServedFromCache = servedFromCache
            };
        }

        public class BeforeCacheResponseData
        {
            public IProxyResponseCachingContext ResponseCachingContext { get; set; }
        }

        public BeforeCacheResponseData BeforeCacheResponse { get; set; }

        [DiagnosticName("Microsoft.AspNetCore.ResponseCaching.BeforeCacheResponse")]
        public virtual void OnBeforeCacheResponse(IProxyResponseCachingContext responseCachingContext)
        {
            BeforeCacheResponse = new BeforeCacheResponseData
            {
                ResponseCachingContext = responseCachingContext
            };
        }

        public class AfterCacheResponseData
        {
            public IProxyResponseCachingContext ResponseCachingContext { get; set; }
        }

        public AfterCacheResponseData AfterCacheResponse { get; set; }

        [DiagnosticName("Microsoft.AspNetCore.ResponseCaching.AfterCacheResponse")]
        public virtual void OnAfterCacheResponse(IProxyResponseCachingContext responseCachingContext)
        {
            AfterCacheResponse = new AfterCacheResponseData
            {
                ResponseCachingContext = responseCachingContext
            };
        }

        public static (DiagnosticSource, TestDiagnosticListener) CreateSourceAndListener(string name = "Microsoft.AspNetCore.ResponseCaching")
        {
            var source = new DiagnosticListener(name);
            var listener = new TestDiagnosticListener();
            source.SubscribeWithAdapter(listener);
            return (source, listener);
        }
    }

    public interface IProxyResponseCachingContext
    {
        
    }
}