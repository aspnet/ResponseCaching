// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel;
using Microsoft.AspNetCore.ResponseCaching.Internal;

namespace Microsoft.AspNetCore.Builder
{
    public class ResponseCacheOptions
    {
        /// <summary>
        /// The largest cacheable size for the response body in bytes. The default is set to 1 MB.
        /// </summary>
        public long MaximumCachedBodySize { get; set; } = 1024 * 1024;

        /// <summary>
        /// The shard size for buffering the response body in bytes. The default is set to 1 KB.
        /// </summary>
        // TODO: Setting to 1KB for testing, 75 KB is more reasonable.
        public int BodyBufferShardSize { get; set; } = 10;

        /// <summary>
        /// <c>true</c> if request paths are case-sensitive; otherwise <c>false</c>. The default is to treat paths as case-insensitive.
        /// </summary>
        public bool UseCaseSensitivePaths { get; set; } = false;

        /// <summary>
        /// For testing purposes only.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal ISystemClock SystemClock { get; set; } = new SystemClock();
    }
}
