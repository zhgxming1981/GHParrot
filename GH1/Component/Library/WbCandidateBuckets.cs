using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    /// <summary>分类和修改预检共用的候选桶，只筛掉不可能匹配的记录。</summary>
    internal static class WbCandidateBuckets
    {
        /// <summary>只包含 Matches 必须相同的字段，用于安全缩小库内候选集。</summary>
        internal readonly struct CandidateBucketKey : IEquatable<CandidateBucketKey>
        {
            private readonly int _holeCount;
            private readonly string _material;
            private readonly string _color;

            public CandidateBucketKey(int holeCount, string material, string color)
            {
                _holeCount = holeCount;
                _material = material;
                _color = color;
            }

            public bool Equals(CandidateBucketKey other) =>
                _holeCount == other._holeCount &&
                string.Equals(_material, other._material, StringComparison.Ordinal) &&
                string.Equals(_color, other._color, StringComparison.Ordinal);

            public override bool Equals(object obj) =>
                obj is CandidateBucketKey other && Equals(other);

            public override int GetHashCode()
            {
                int hash = _holeCount;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_material ?? string.Empty);
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_color ?? string.Empty);
            }
        }

        internal static Dictionary<CandidateBucketKey, List<WbMigrationRecord>> BuildCandidateBuckets(
            List<WbMigrationRecord> records, bool strictMaterialColor)
        {
            var buckets = new Dictionary<CandidateBucketKey, List<WbMigrationRecord>>();
            if (records == null) return buckets;

            foreach (WbMigrationRecord record in records)
            {
                CandidateBucketKey key = CreateCandidateBucketKey(record.Feature, strictMaterialColor);
                if (!buckets.TryGetValue(key, out List<WbMigrationRecord> bucket))
                {
                    bucket = new List<WbMigrationRecord>();
                    buckets.Add(key, bucket);
                }
                bucket.Add(record);
            }
            return buckets;
        }

        internal static CandidateBucketKey CreateCandidateBucketKey(WbFeature feature, bool strictMaterialColor)
        {
            int holeCount = feature?.Holes?.Count ?? 0;
            if (!strictMaterialColor)
                return new CandidateBucketKey(holeCount, string.Empty, string.Empty);

            return new CandidateBucketKey(holeCount,
                WbUtil.NormalizeOptionalMatchText(feature?.Material),
                WbUtil.NormalizeOptionalMatchText(feature?.Color));
        }
    }
}
