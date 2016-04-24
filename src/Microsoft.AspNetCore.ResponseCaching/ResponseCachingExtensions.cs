﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.ResponseCaching;

namespace Microsoft.AspNetCore.Builder
{
    public static class ResponseCachingExtensions
    {
        public static IApplicationBuilder UseResponseCaching(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ResponseCachingMiddleware>();
        }
    }
}
