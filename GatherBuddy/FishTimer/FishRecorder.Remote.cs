using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GatherBuddy.Classes;
using GatherBuddy.Utilities;
using GatherBuddy.Models;
using Lumina.Data.Parsing;
using Newtonsoft.Json;

namespace GatherBuddy.FishTimer;

public partial class FishRecorder
{
    public const  string                  RemoteUrl                 = "https://sevxzz9056.execute-api.us-east-1.amazonaws.com/fishwrap";
    public const  string                  RemoteFishRecordsFileName = "GatherBuddy.CustomInfo.fish_records.json";
    private const int                     RecordsPerRequest         = 100;
    internal      List<FishRecord>        RemoteRecords             = [];
    internal      Queue<FishRecord>       RecordsToUpload           = new();
    public        Task                    RemoteRecordsUploadTask              { get; private set; } = Task.CompletedTask;
    private       CancellationTokenSource RemoteRecordsCancellationTokenSource { get; }              = new();
    public        DateTime                NextRemoteRecordsUpdate = DateTime.MinValue;

    public bool UploadTaskReady
        => RemoteRecordsUploadTask.IsCompleted;

    public void StopRemoteRecordsRequests()
    {
        RemoteRecordsCancellationTokenSource.Cancel();
        WriteRemoteFile();
    }

    public void QueueHistoricalRecords()
    {
        var records = Records.Where(r => r.PositionDataValid).ToList();
        foreach (var record in records)
        {
            RecordsToUpload.Enqueue(record);
        }
        GatherBuddy.Log.Information($"已将 {records.Count()} 条记录加入上传队列");
    }

    private readonly Random _random = new(Guid.NewGuid().GetHashCode());
    public (Vector3 Position, Angle Rotation)? GetPositionForFishingSpot(FishingSpot spot)
    {
        var allValidRecords = RemoteRecords.Union(Records).Where(r => r.FishingSpot == spot && r.PositionDataValid);
        if (!allValidRecords.Any())
            return null;

        var random = _random.Next(0, allValidRecords.Count());
        var selectedRecord = allValidRecords.ElementAt(random);
        return (selectedRecord.Position, selectedRecord.RotationAngle);
    }

    public (Vector3 Position, Angle Rotation)? GetPositionForFishingSpot(FishingSpot spot, Vector3 avoidPosition, float minDistance)
    {
        var allValidRecords = RemoteRecords.Union(Records).Where(r => r.FishingSpot == spot && r.PositionDataValid).ToList();
        if (allValidRecords.Count == 0)
            return null;

        var farEnough = allValidRecords
            .Where(r => Vector3.Distance(r.Position, avoidPosition) >= minDistance)
            .ToList();

        FishRecord selectedRecord;
        if (farEnough.Count > 0)
        {
            var random = _random.Next(0, farEnough.Count);
            selectedRecord = farEnough[random];
        }
        else
        {
            selectedRecord = allValidRecords
                .OrderByDescending(r => Vector3.Distance(r.Position, avoidPosition))
                .First();
        }

        return (selectedRecord.Position, selectedRecord.RotationAngle);
    }

    private async Task UploadLocalRecords(CancellationToken cancellationToken = default)
    {
        if (RecordsToUpload.Count == 0)
        {
            GatherBuddy.Log.Verbose("没有需要上传的记录");
        }
        else
        {
            try
            {
                var records = new List<FishRecord>();
                while (RecordsToUpload.TryDequeue(out var record) && records.Count < RecordsPerRequest)
                {
                    records.Add(record);
                }

                GatherBuddy.Log.Debug($"正在上传 {records.Count} 条本地钓鱼记录到远程服务器");
                var simpleRecords = new List<SimpleFishRecord>(records.Count);
                foreach (var record in records)
                {
                    var simpleRecord = record.ToSimpleRecord();
                    simpleRecords.Add(simpleRecord);
                }

                var       json     = JsonConvert.SerializeObject(simpleRecords);
                var       content  = new StringContent(json, Encoding.UTF8, "application/json");
                using var client   = new FishRecorderClient();
                var       response = await client.PostAsync(RemoteUrl, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    GatherBuddy.Log.Error($"无法上传本地钓鱼记录: {response.StatusCode}");
                GatherBuddy.Log.Information($"已上传 {records.Count} 条本地钓鱼记录到远程服务器");
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"无法上传本地钓鱼记录:\n{e}");
            }
        }

        NextRemoteRecordsUpdate = DateTime.Now.AddMinutes(1);
    }


    private void LoadRemoteFile()
    {
        try
        {
            var embeddedResource = typeof(FishRecorder).Assembly.GetManifestResourceStream(RemoteFishRecordsFileName);
            if (embeddedResource == null)
                throw new FileNotFoundException($"找不到嵌入资源 {RemoteFishRecordsFileName}");
            using var reader = new StreamReader(embeddedResource);
            var       json   = reader.ReadToEnd();
            var       records = JsonConvert.DeserializeObject<List<SimpleFishRecord>>(json);
            if (records == null)
                throw new JsonException("无法反序列化远程钓鱼记录");

            foreach (var record in records)
            {
                var fishRecord = FishRecord.FromSimpleRecord(record);
                RemoteRecords.Add(fishRecord);
            }
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"无法读取钓鱼记录文件 {FishRecordFileName}:\n{e}");
        }
    }

    private void WriteRemoteFile()
    {
        var file = new FileInfo(Path.Combine(FishRecordDirectory.FullName, RemoteFishRecordsFileName));
        WriteFileInternal(file, true);
    }

    private class FishRecorderClient : HttpClient
    {
        internal FishRecorderClient()
            : base(new RateLimitingHandler(new HttpClientHandler()))
        {
            var version = typeof(GatherBuddy).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            DefaultRequestHeaders.Add("X-Client-Version", version);

            DefaultRequestHeaders.UserAgent.Clear();
            DefaultRequestHeaders.UserAgent.ParseAdd($"GatherBuddyReborn/{version} (https://github.com/FFXIV-CombatReborn/GatherBuddyReborn)");
        }
    }

    private class RateLimitingHandler : DelegatingHandler
    {
        internal RateLimitingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests) // 429
                {
                    if (response.Headers.TryGetValues("X-Rate-Limit-Reset", out var values)
                     && int.TryParse(values.FirstOrDefault(), out var retrySeconds))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    }
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    return response;
                }
            }
        }
    }
}
