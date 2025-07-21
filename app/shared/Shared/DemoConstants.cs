// Copyright (c) Microsoft. All rights reserved.

namespace Shared;
internal class DemoConstants
{
    public class SimpleFields
    {
        public const string Id = "id";
        public const string Category = "category";
        public const string SourcePage = "sourcepage";
        public const string SourceFile = "sourcefile";
    }

    public class EmbeddingFields
    {
        public const string Docs = "embedding";
        public const string Images = "imageEmbedding";
    }

    public class Semantic
    {
        public const string ConfigName = "default";
        public const string SearchableField = "content";
    }

    public class Category
    {
        public const string Docs = "docs";
        public const string Images = "image";
    }

    public class VectorSearch
    {
        public const string ConfigName = "my-vector-config";
        public const string Profile = "my-vector-profile";
        public const int DocsDimensions = 1536;
        public static int? ImageDimensions = null;
    }
}
