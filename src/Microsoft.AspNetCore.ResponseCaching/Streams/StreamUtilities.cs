﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal static class StreamUtilities
    {
        /// <summary>
        /// The segment size for buffering the response body in bytes. The default is set to 84 KB.
        /// </summary>
        // Internal for testing
        internal static int BodySegmentSize { get; set; } = 84 * 1024;

        internal static IAsyncResult ToIAsyncResult(Task task, AsyncCallback callback, object state)
        {
            var tcs = new TaskCompletionSource<int>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    tcs.TrySetException(t.Exception.InnerExceptions);
                }
                else if (t.IsCanceled)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    tcs.TrySetResult(0);
                }

                callback?.Invoke(tcs.Task);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return tcs.Task;
        }
    }
}
