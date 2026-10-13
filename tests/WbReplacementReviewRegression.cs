using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;

// 在临时数据库上调用已构建的生产程序集，不加载或修改用户数据库。
public static class WbReplacementReviewRegression
{
    private static Assembly assembly;
    private static int checks;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Type T(string name) { return assembly.GetType("NS_Parrot." + name, true); }
    private static object Call(string type, string method, params object[] args)
    {
        return T(type).GetMethods(Flags).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(null, args);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static object Get(object value, string field) { return value.GetType().GetField(field, Flags).GetValue(value); }
    private static void Set(object value, string field, object data) { value.GetType().GetField(field, Flags).SetValue(value, data); }
    private static IList Records() { return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(T("WbMigrationRecord"))); }
    private static object Record(string number, object feature)
    {
        return Activator.CreateInstance(T("WbMigrationRecord"), new object[] { 0, number, feature, "" });
    }
    private static object Feature(string material, string color, int holeCount = 0, double width = 10)
    {
        object feature = Activator.CreateInstance(T("WbFeature"), true);
        Set(feature, "Material", material);
        Set(feature, "Color", color);
        Set(feature, "BoundingBox", new double[] { width, 5, 2 });
        Set(feature, "Inertia", new double[] { 9, 4, 1 });
        Set(feature, "Area", 100d);
        Set(feature, "Volume", 50d);
        var holes = (IList)Get(feature, "Holes");
        for (int i = 0; i < holeCount; i++)
        {
            object hole = Activator.CreateInstance(T("WbHole"), new object[] {
                4d, 1d, (double)i, 0d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, "", false, "" });
            holes.Add(hole);
        }
        return feature;
    }
    private static string Serialize(object feature)
    {
        return (string)feature.GetType().GetMethod("Serialize", Type.EmptyTypes).Invoke(feature, null);
    }
    private static SQLiteConnection Open(string path)
    {
        var connection = new SQLiteConnection("Data Source=" + path + ";Version=3;Pooling=False;");
        connection.Open();
        return connection;
    }
    private static void Seed(string path)
    {
        using (var c = Open(path))
        using (var tx = c.BeginTransaction())
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO parts(profile_id,part_number,material,color,entity_type,is_solid,volume,surface_area,bbox_max,bbox_mid,bbox_min,bbox_count,inertia_1,inertia_2,inertia_3,hole_count,hole_perimeter_sum,hole_area_sum,source_file,source_index) VALUES(1,@n,'原材质','原颜色',1,1,50,100,10,5,2,3,9,4,1,0,0,0,'',0);";
            for (int i = 0; i < 805; i++)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@n", "A" + i);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }
    private static void Stored(string path, string number, string material, string color)
    {
        using (var c = Open(path))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT material,color,feature_text FROM parts p JOIN part_features f ON p.part_id=f.part_id WHERE part_number=@n;";
            cmd.Parameters.AddWithValue("@n", number);
            using (var r = cmd.ExecuteReader())
            {
                Check(r.Read(), "缺少完整特征");
                Check(r.GetString(0) == material && r.GetString(1) == color, "主表材质颜色不符");
                object restored = Call("WbFeature", "Deserialize", r.GetString(2));
                Check((string)Get(restored, "Material") == material && (string)Get(restored, "Color") == color, "完整特征与主表不一致");
            }
        }
    }
    private static void Validate(string path, IEnumerable<string> numbers, bool expected, string text)
    {
        object[] args = { path, numbers, null };
        bool result = (bool)Call("WbMigrationStore", "TryValidateReplacementTargets", args);
        Check(result == expected && (expected || ((string)args[2]).Contains(text)), "批量预检结果不符：" + args[2]);
    }
    private static bool Matches(object a, object b, object settings, bool strict)
    {
        Func<string, object> property = name => {
            var member = settings.GetType().GetProperty(name, Flags);
            return member == null ? Get(settings, name) : member.GetValue(settings, null);
        };
        return (bool)Call("WbMigrationAudit", "Matches", a, b,
            property("Enabled"), property("Tolerances"), property("IsRelativeTolerance"), strict,
            property("HoleNormalTolerance"), property("OuterProfileAreaRatio"), property("OuterProfilePerimeterMin"),
            property("DegeneratePositionMultiplier"), property("EnableDegenerateMatching"), property("FallbackTolerance"));
    }
    public static void Run(string gha, string directory)
    {
        assembly = Assembly.LoadFrom(gha);
        string path = Path.Combine(directory, "replacement.db");
        Call("WbMigrationStore", "CreateEmpty", path);
        Seed(path);
        Validate(path, Enumerable.Range(0, 805).Select(i => "a" + i), true, null);
        Validate(path, new[] { "A0", "missing" }, false, "未找到编号：missing");
        Validate(path, new[] { " A0" }, false, "未找到编号");
        Validate(path, new[] { "A0", "a0" }, true, null);
        string abnormal = Path.Combine(directory, "abnormal.db");
        using (var c = Open(abnormal))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE parts(part_id INTEGER PRIMARY KEY,part_number TEXT); INSERT INTO parts VALUES(1,'A'),(2,'a'),(3,'Ä');";
            cmd.ExecuteNonQuery();
        }
        Validate(abnormal, new[] { "A" }, false, "命中多条");
        Validate(abnormal, new[] { "ä" }, false, "未找到编号");
        Validate(abnormal, new[] { "Ä" }, true, null);

        object feature = Feature("", " \t");
        string original = Serialize(feature);
        IList records = Records();
        records.Add(Record("A0", feature));
        Call("WbMigrationStore", "ReplacePartsBatch", path, records);
        Check(Serialize(feature) == original, "保留空值时修改了调用方对象");
        Stored(path, "A0", "原材质", "原颜色");
        Set(feature, "Material", "新材质");
        Set(feature, "Color", "");
        original = Serialize(feature);
        Call("WbMigrationStore", "ReplacePartsBatch", path, records);
        Check(Serialize(feature) == original, "更新材质时修改了调用方对象");
        Stored(path, "A0", "新材质", "原颜色");
        Set(feature, "Material", null);
        Set(feature, "Color", "新颜色");
        Call("WbMigrationStore", "ReplacePartsBatch", path, records);
        Stored(path, "A0", "新材质", "新颜色");
        Set(feature, "Material", "不应提交");
        original = Serialize(feature);
        records.Add(Record("missing", Feature("", "")));
        bool failed = false;
        try { Call("WbMigrationStore", "ReplacePartsBatch", path, records); }
        catch (TargetInvocationException ex) { failed = ex.InnerException is InvalidOperationException; }
        Check(failed, "缺失编号未阻止提交");
        Check(Serialize(feature) == original, "回滚后入参已改变");
        Stored(path, "A0", "新材质", "新颜色");

        IList samples = Records();
        string[] texts = { "", " ", "钢", "铝", "a", "A" };
        for (int i = 0; i < 72; i++)
            samples.Add(Record("S" + i, Feature(texts[i % 6], texts[(i / 6) % 6], (i / 12) % 3, i % 2 == 0 ? 10 : 20)));
        object settings = Activator.CreateInstance(T("WbClassificationSettings"), true);
        foreach (bool strict in new[] { true, false })
        {
            var buckets = (IDictionary)Call("WbCandidateBuckets", "BuildCandidateBuckets", samples, strict);
            foreach (object sample in samples)
            {
                object input = Get(sample, "Feature");
                object key = Call("WbCandidateBuckets", "CreateCandidateBucketKey", input, strict);
                var filtered = (IList)buckets[key];
                var fullMatches = samples.Cast<object>().Where(x => Matches(input, Get(x, "Feature"), settings, strict)).Select(x => (string)Get(x, "Number")).ToArray();
                var bucketMatches = filtered.Cast<object>().Where(x => Matches(input, Get(x, "Feature"), settings, strict)).Select(x => (string)Get(x, "Number")).ToArray();
                Check(fullMatches.SequenceEqual(bucketMatches), "分桶改变了匹配结果或顺序");
            }
        }
        Console.WriteLine("PASS: " + checks + " checks; batch validation, storage consistency, rollback, input immutability, candidate equivalence.");
    }
}
