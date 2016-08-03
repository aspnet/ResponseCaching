﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.ResponseCaching
{
    internal class ResponseCachingEntry
    {
        public int StatusCode { get; set; }
        internal IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        internal byte[] Body { get; set; }
        public DateTimeOffset Created { get; set; }
    }
}
