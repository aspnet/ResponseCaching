// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class SerializableCachedResponse : IResponseCacheEntry
    {
        internal CachedResponse CachedResponse { get; set; }

        internal string ShardKeyPrefix { get; set; }

        internal long ShardCount { get; set; }

        internal long BodyLength { get; set; }
    }
}
