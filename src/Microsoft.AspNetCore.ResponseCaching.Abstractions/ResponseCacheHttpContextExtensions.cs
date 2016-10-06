﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.ResponseCaching
{
    public static class ResponseCacheHttpContextExtensions
    {
        public static IResponseCacheFeature GetResponseCacheFeature(this HttpContext httpContext)
        {
            return httpContext.Features.Get<IResponseCacheFeature>();
        }
    }
}
