using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.ResponseCaching
{
    interface IResponseCacheOptions
    {
        int MaxCachedItemBytes { get; set; }
    }
}
