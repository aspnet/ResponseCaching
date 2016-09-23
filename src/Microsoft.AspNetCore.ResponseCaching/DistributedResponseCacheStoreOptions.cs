// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.ResponseCaching
{
    public class DistributedResponseCacheStoreOptions
    {
        /// <summary>
        /// The shard size for storing the response body in the distributed cache. The default is set to ? KB.
        /// </summary>
        // TODO: Setting to 5 for testing, need to set a reasonable default.
        public int DistributedCacheBodyShardSize { get; set; } = 5;
    }
}
