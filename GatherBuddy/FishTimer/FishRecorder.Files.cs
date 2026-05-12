using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GatherBuddy.Plugin;
using GatherBuddy.Time;
using MessagePack;
using Newtonsoft.Json;

namespace GatherBuddy.FishTimer;

public partial class FishRecorder
{
    public const    string        FishRecordFileName = "fish_records.dat";
    public readonly DirectoryInfo FishRecordDirectory;

    public  int       Changes  = 0;
    public  TimeStamp SaveTime = TimeStamp.MaxValue;

    public int AddChanges()
    {
        SaveTime = TimeStamp.UtcNow.AddMinutes(3);
        return ++Changes;
    }

    public void WriteFile()
    {
        var file = new FileInfo(Path.Combine(FishRecordDirectory.FullName, FishRecordFileName));
        Changes  = 0;
        SaveTime = TimeStamp.MaxValue;
        WriteFileInternal(file, false);
    }

    private void WriteFileInternal(FileInfo file, bool remote)
    {
        GatherBuddy.Log.Debug($"正在保存钓鱼记录文件到 {file.FullName}，有 {Changes} 项变更");
        try
        {
            var bytes = GetRecordBytes(remote);
            File.WriteAllBytes(file.FullName, bytes);
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"无法写入钓鱼记录文件 {file.FullName}:\n{e}");
        }
    }

    private void TimedSave()
    {
        if (TimeStamp.UtcNow > SaveTime)
        {
            WriteFile();
        }
    }

    public string ExportBase64()
    {
        var bytes = GetRecordBytes(false);
        return Functions.CompressedBase64(bytes);
    }

    public void ExportJson(FileInfo file)
    {
        try
        {
            var data = JsonConvert.SerializeObject(Records.Select(r => r.ToJson()), Formatting.Indented);
            File.WriteAllText(file.FullName, data);
            GatherBuddy.Log.Information($"已导出 {Records.Count} 条钓鱼记录到 {file.FullName}");
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Warning($"无法导出 JSON 文件到 {file.FullName}:\n{e}");
        }
    }

    public void ImportBase64(string data)
    {
        try
        {
            var bytes   = Functions.DecompressedBase64(data);
            var records = ReadBytes(bytes, "Imported Data");
            MergeRecordsIn(records);
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Warning($"导入钓鱼记录时出错:\n{e}");
        }
    }

    public void MergeRecordsIn(IReadOnlyList<FishRecord> newRecords)
    {
        foreach (var record in newRecords.Where(CheckSimilarity))
            AddUnchecked(record);

        if (Changes > 0)
            WriteFile();
    }

    public static List<FishRecord> ReadFile(FileInfo file)
    {
        if (!file.Exists)
            return new List<FishRecord>();

        try
        {
            var bytes = File.ReadAllBytes(file.FullName);
            return ReadBytes(bytes, $"File {file.FullName}");
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"读取钓鱼记录文件 {file.FullName} 时发生未知错误:\n{e}");
            return new List<FishRecord>();
        }
    }

    private byte[] GetRecordBytes(bool remote)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(FishRecord.Version);

        var records = remote ? RemoteRecords : Records;
        MessagePackSerializer.Serialize(ms, records);

        return ms.ToArray();
    }

    private static List<FishRecord> ReadBytes(byte[] data, string name)
    {
        if (data.Length == 0)
            return new List<FishRecord>();

        switch (data[0])
        {
            case 1:
            {
                if (data.Length % FishRecord.Version1ByteLength != 1)
                {
                    GatherBuddy.Log.Error($"{name} 的记录版本大小无效，已跳过\n");
                    return new List<FishRecord>();
                }

                var numRecords = (data.Length - 1) / FishRecord.Version1ByteLength;
                var ret        = new List<FishRecord>(numRecords);
                for (var i = 0; i < numRecords; ++i)
                {
                    if (!FishRecord.FromBytesV1(data, 1 + i * FishRecord.Version1ByteLength, out var record))
                    {
                        GatherBuddy.Log.Error($"{name} 的第 {i} 条记录无效，已跳过\n");
                        continue;
                    }

                    ret.Add(record);
                }

                return ret;
            }
            case 2:
            {
                var span = data.AsSpan()[1..];
                try
                {
                    var list = MessagePackSerializer.Deserialize<List<FishRecord>>(span.ToArray());
                    return list;
                }
                catch (Exception e)
                {
                    GatherBuddy.Log.Error($"{name} 无法使用 V2 逻辑反序列化");
                    return new List<FishRecord>();
                }
            }
            default:
                GatherBuddy.Log.Error($"{name} 没有有效的记录版本，已跳过\n");
                return new List<FishRecord>();
        }
    }

    private void LoadFile(FileInfo file)
    {
        if (!file.Exists)
            return;

        try
        {
            Records.AddRange(ReadFile(file));
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"无法读取钓鱼记录文件 {file.FullName}:\n{e}");
        }
        ResetTimes();
    }
}
