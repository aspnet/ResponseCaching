﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal static class CacheEntrySerializer
    {
        private const int FormatVersion = 1;

        public static object Deserialize(byte[] serializedEntry)
        {
            if (serializedEntry == null)
            {
                return null;
            }

            using (var memory = new MemoryStream(serializedEntry))
            {
                using (var reader = new BinaryReader(memory))
                {
                    return Read(reader);
                }
            }
        }

        public static byte[] Serialize(object entry)
        {
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory))
                {
                    Write(writer, entry);
                    writer.Flush();
                    return memory.ToArray();
                }
            }
        }

        // Serialization Format
        // Format version (int)
        // Type (char: 'R' for CachedResponse, 'V' for CachedVaryByRules)
        // Type-dependent data (see CachedResponse and CachedVaryByRules)
        public static object Read(BinaryReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (reader.ReadInt32() != FormatVersion)
            {
                return null;
            }

            var type = reader.ReadChar();

            if (type == 'R')
            {
                return ReadCachedResponse(reader);
            }
            else if (type == 'V')
            {
                return ReadCachedVaryByRules(reader);
            }

            // Unable to read as CachedResponse or CachedVaryByRules
            return null;
        }

        // Serialization Format
        // Creation time - UtcTicks (long)
        // Status code (int)
        // Header count (int)
        // Header(s)
        //   Key (string)
        //   ValueCount (int)
        //   Value(s)
        //     Value (string)
        // Body length (long)
        // Body (byte[])
        private static CachedResponse ReadCachedResponse(BinaryReader reader)
        {
            var created = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var statusCode = reader.ReadInt32();
            var headerCount = reader.ReadInt32();
            var headers = new HeaderDictionary();
            for (var index = 0; index < headerCount; index++)
            {
                var key = reader.ReadString();
                var headerValueCount = reader.ReadInt32();
                if (headerValueCount > 1)
                {
                    var headerValues = new string[headerValueCount];
                    for (var valueIndex = 0; valueIndex < headerValueCount; valueIndex++)
                    {
                        headerValues[valueIndex] = reader.ReadString();
                    }
                    headers[key] = headerValues;
                }
                else if (headerValueCount == 1)
                {
                    headers[key] = reader.ReadString();
                }
            }

            var bodyLength = reader.ReadInt64();
            var body = new MemoryStream(reader.ReadBytes((int)bodyLength));

            return new CachedResponse { Created = created, StatusCode = statusCode, Headers = headers, Body = body };
        }

        // Serialization Format
        // Guid (long)
        // Headers count
        // Header(s) (comma separated string)
        // QueryKey count
        // QueryKey(s) (comma separated string)
        private static CachedVaryByRules ReadCachedVaryByRules(BinaryReader reader)
        {
            var varyKeyPrefix = reader.ReadString();

            var headerCount = reader.ReadInt32();
            var headers = new string[headerCount];
            for (var index = 0; index < headerCount; index++)
            {
                headers[index] = reader.ReadString();
            }
            var queryKeysCount = reader.ReadInt32();
            var queryKeys = new string[queryKeysCount];
            for (var index = 0; index < queryKeysCount; index++)
            {
                queryKeys[index] = reader.ReadString();
            }

            return new CachedVaryByRules { VaryByKeyPrefix = varyKeyPrefix, Headers = headers, QueryKeys = queryKeys };
        }

        // See serialization format above
        public static void Write(BinaryWriter writer, object entry)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            writer.Write(FormatVersion);

            if (entry is CachedResponse)
            {
                writer.Write('R');
                WriteCachedResponse(writer, entry as CachedResponse);
            }
            else if (entry is CachedVaryByRules)
            {
                writer.Write('V');
                WriteCachedVaryByRules(writer, entry as CachedVaryByRules);
            }
            else
            {
                throw new NotSupportedException($"Unrecognized entry format for {nameof(entry)}.");
            }
        }

        // See serialization format above
        private static void WriteCachedResponse(BinaryWriter writer, CachedResponse entry)
        {
            writer.Write(entry.Created.UtcTicks);
            writer.Write(entry.StatusCode);
            writer.Write(entry.Headers.Count);
            foreach (var header in entry.Headers)
            {
                writer.Write(header.Key);
                writer.Write(header.Value.Count);
                foreach (var headerValue in header.Value)
                {
                    writer.Write(headerValue);
                }
            }

            writer.Write(entry.Body.Length);
            writer.Write(entry.Body.ToArray());
        }

        // See serialization format above
        private static void WriteCachedVaryByRules(BinaryWriter writer, CachedVaryByRules varyByRules)
        {
            writer.Write(varyByRules.VaryByKeyPrefix);

            writer.Write(varyByRules.Headers.Count);
            foreach (var header in varyByRules.Headers)
            {
                writer.Write(header);
            }

            writer.Write(varyByRules.QueryKeys.Count);
            foreach (var queryKey in varyByRules.QueryKeys)
            {
                writer.Write(queryKey);
            }
        }
    }
}
