// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Extensions;
public static class BlobExtension
{
    public static TEnum GetMetadataEnumOrDefault<TEnum>(
                     IDictionary<string, string> metadata,
                     string key,
                     TEnum @default) where TEnum : struct => metadata.TryGetValue(key, out var value)
                         && Enum.TryParse<TEnum>(value, out var status)
                             ? status
                             : @default;
}
