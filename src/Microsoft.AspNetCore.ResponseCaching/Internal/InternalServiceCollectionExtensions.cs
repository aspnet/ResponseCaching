// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    internal static class InternalServiceCollectionExtensions
    {
        internal static IServiceCollection AddDistributedResponseCacheStore(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddDistributedMemoryCache();
            services.TryAdd(ServiceDescriptor.Singleton<IResponseCachePolicyProvider, ResponseCachePolicyProvider>());
            services.TryAdd(ServiceDescriptor.Singleton<IResponseCacheStore, DistributedResponseCacheStore>());

            return services;
        }
    }
}
