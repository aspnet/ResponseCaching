﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    internal class TestUtils
    {
        static TestUtils()
        {
            // Force sharding in tests
            StreamUtilities.BodySegmentSize = 10;
        }

        internal static RequestDelegate TestRequestDelegate = async (context) =>
        {
            var uniqueId = Guid.NewGuid().ToString();
            var headers = context.Response.GetTypedHeaders();
            headers.CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                MaxAge = TimeSpan.FromSeconds(10)
            };
            headers.Date = DateTimeOffset.UtcNow;
            headers.Headers["X-Value"] = uniqueId;
            await context.Response.WriteAsync(uniqueId);
        };

        internal static ResponseCacheKeyProvider CreateTestKeyProvider()
        {
            return CreateTestKeyProvider(new ResponseCacheOptions());
        }

        internal static ResponseCacheKeyProvider CreateTestKeyProvider(ResponseCacheOptions options)
        {
            return new ResponseCacheKeyProvider(new DefaultObjectPoolProvider(), options);
        }

        internal static IEnumerable<IWebHostBuilder> CreateBuildersWithResponseCache(
            Action<IApplicationBuilder> configureDelegate = null,
            ResponseCacheOptions options = null,
            RequestDelegate requestDelegate = null)
        {
            if (configureDelegate == null)
            {
                configureDelegate = app => { };
            }
            if (options == null)
            {
                options = new ResponseCacheOptions();
            }
            if (requestDelegate == null)
            {
                requestDelegate = TestRequestDelegate;
            }

            // Test with MemoryResponseCacheStore
            yield return new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMemoryResponseCacheStore();
                })
                .Configure(app =>
                {
                    configureDelegate(app);
                    app.UseResponseCache(options);
                    app.Run(requestDelegate);
                });

            // Test with DistributedResponseCacheStore
            yield return new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddDistributedResponseCacheStore();
                })
                .Configure(app =>
                {
                    configureDelegate(app);
                    app.UseResponseCache(options);
                    app.Run(requestDelegate);
                });
        }

        internal static ResponseCacheMiddleware CreateTestMiddleware(
            IResponseCacheStore store = null,
            ResponseCacheOptions options = null,
            IResponseCachePolicyProvider policyProvider = null)
        {
            if (store == null)
            {
                store = new TestResponseCacheStore();
            }
            if (options == null)
            {
                options = new ResponseCacheOptions();
            }
            if (policyProvider == null)
            {
                policyProvider = new TestResponseCachePolicyProvider();
            }

            return new ResponseCacheMiddleware(
                httpContext => TaskCache.CompletedTask,
                store,
                Options.Create(options),
                policyProvider,
                new DefaultObjectPoolProvider());
        }

        internal static ResponseCacheContext CreateTestContext()
        {
            return new ResponseCacheContext(new DefaultHttpContext())
            {
                ResponseTime = DateTimeOffset.UtcNow
            };
        }
    }

    internal class DummySendFileFeature : IHttpSendFileFeature
    {
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellation)
        {
            return TaskCache.CompletedTask;
        }
    }

    internal class TestResponseCachePolicyProvider : IResponseCachePolicyProvider
    {
        public bool IsCachedEntryFresh(ResponseCacheContext context) => true;

        public bool IsRequestCacheable(ResponseCacheContext context) => true;

        public bool IsResponseCacheable(ResponseCacheContext context) => true;
    }

    internal class TestResponseCacheStore : IResponseCacheStore
    {
        private readonly IDictionary<string, IResponseCacheEntry> _storage = new Dictionary<string, IResponseCacheEntry>();
        public int GetCount { get; private set; }
        public int SetCount { get; private set; }

        public Task<IResponseCacheEntry> GetAsync(string key)
        {
            GetCount++;
            try
            {
                return Task.FromResult(_storage[key]);
            }
            catch
            {
                return Task.FromResult<IResponseCacheEntry>(null);
            }
        }

        public Task SetAsync(string key, IResponseCacheEntry entry, TimeSpan validFor)
        {
            SetCount++;
            _storage[key] = entry;
            return TaskCache.CompletedTask;
        }
    }
}
