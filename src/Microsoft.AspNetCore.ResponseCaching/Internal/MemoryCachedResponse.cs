// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class MemoryCachedResponse
    {
        public DateTimeOffset Created { get; set; }

        public int StatusCode { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public List<byte[]> Shards { get; set; }

        public int ShardSize { get; set; }

        public long BodyLength { get; set; }
    }
}
