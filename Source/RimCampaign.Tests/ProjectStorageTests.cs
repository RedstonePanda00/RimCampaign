using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using RimCampaign.Core;

static class ProjectStorageTests
{
    [DataContract]
    sealed class BrokenDocument
    {
        [DataMember] public string Text { get => throw new InvalidOperationException("serialization failed"); set { } }
    }

    public static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetTempPath(), "RimCampaign-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            var map = new MapSpec { Id = "map", Name = "地图 · α", Notes = "Keep authored text\nunchanged." };
            for (int i = 0; i < 1000; i++) map.Objects.Add(new Placement {
                Id = "terrain-" + i, Kind = "Terrain", Layer = i % 2 == 0 ? "Terrain" : "Floor",
                Position = new Cell { X = i % 50, Z = i / 50 },
                Def = new DefRef { Kind = "TerrainDef", Name = "Soil" }, Faction = "Neutral"
            });
            var project = new Project { Id = "storage", Maps = new List<MapSpec> { map } };
            var plain = Path.Combine(folder, "author.project.json");
            var packed = plain + ".gz";
            Json.Save(plain, project);
            Json.Save(packed, project);
            var bytes = File.ReadAllBytes(packed);
            check(bytes[0] == 0x1f && bytes[1] == 0x8b, "compressed project is a gzip stream");
            check(bytes.Length < new FileInfo(plain).Length / 5, "repeated map records shrink substantially on disk");
            check(Json.Write(Json.Load<Project>(packed)) == Json.Write(Json.Load<Project>(plain)), "plain and compressed projects preserve every serialized field");
            var snapshot = Path.Combine(folder, "latest.project.json.gz");
            Json.CopyFile(plain, snapshot);
            check(Json.Write(Json.Load<Project>(snapshot)) == Json.Write(project), "hot test compresses a plain authoring project without changing it");
            var unpacked = Path.Combine(folder, "unpacked.project.json");
            Json.CopyFile(snapshot, unpacked);
            check(File.ReadAllBytes(plain).SequenceEqual(File.ReadAllBytes(unpacked)), "stream conversion preserves the exact JSON bytes");
            var renamed = Path.Combine(folder, "copied.project.json");
            File.Copy(snapshot, renamed);
            check(Json.Load<Project>(renamed).Maps[0].Objects.Count == 1000, "loader detects compressed contents even in copied JSON paths");
            bool failed = false;
            try { Json.Save(packed, new BrokenDocument()); } catch (Exception) { failed = true; }
            check(failed && File.ReadAllBytes(packed).SequenceEqual(bytes), "failed serialization preserves the existing project file");
            check(!Directory.GetFiles(folder, "*.tmp-*").Any(), "failed serialization leaves no temporary document");
            File.WriteAllBytes(snapshot, new byte[] { 0x1f, 0x8b, 8, 0, 0 });
            failed = false;
            try { Json.Load<Project>(snapshot); } catch (Exception) { failed = true; }
            check(failed, "truncated compressed project is rejected");
        } finally { Directory.Delete(folder, true); }
    }
}
