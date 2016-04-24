// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.ResponseCaching
{
    public class ResponseCacheEntry
    {
        public int StatusCode { get; set; }
        public List<KeyValuePair<string, StringValues>> Headers { get; set; }
        public byte[] Body { get; set; }
    }
}
