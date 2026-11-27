using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Yxrl.Capture;

internal enum RecognitionKind { Document, Table, Formula }

internal readonly record struct ModelDownload(long Received, long Total);

internal sealed class RecognitionModels(string directory)
{
    private sealed record Asset(string Name, string Url, long Size, string Hash);
    private const string FormulaUrl = "https://huggingface.co/breezedeus/pix2text-mfr-1.5/resolve/1cef9f0bdcd6a4c63df7de1311fb0894593340cc/";
    private static readonly Asset[] Document =
    [
        new("layout_cdla.onnx", "https://www.modelscope.cn/models/RapidAI/RapidLayout/resolve/v1.2.0/onnx/pp_layout/layout_cdla.onnx",
            7423528, "25B1F27EC56AA932A48F30CBD6293C358A156280F4B20B0A973BAB210C39F62C")
    ];
    private static readonly Asset[] Table =
    [
        new("slanet-plus.onnx", "https://www.modelscope.cn/models/RapidAI/RapidTable/resolve/v2.0.0/slanet-plus.onnx",
            7758305, "D57A942AF6A2F57D6A4A0372573C696A2379BF5857C45E2AC69993F3B334514B")
    ];
    private static readonly Asset[] Formula =
    [
        new("encoder_model.onnx", FormulaUrl + "encoder_model.onnx", 87510770, "080A3F660F08BC9EBCACDD96E34BE6B6400F8C7E62D7CD0DD8251BADC37F610B"),
        new("decoder_model.onnx", FormulaUrl + "decoder_model.onnx", 32026253, "917DEB98E91A0453C5F234F58A0F32F9FB037DE8527C7EB4ED394DAF9E692F2A"),
        new("tokenizer.json", FormulaUrl + "tokenizer.json", 113168, "4FFBEB2143E6A38324BB6111B7A8109530D38A076A8439AA5777535F0A32758A")
    ];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly SemaphoreSlim installGate = new(1);

    static RecognitionModels()
    {
        var version = typeof(RecognitionModels).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("YXRL/" + version);
    }

    private static Asset[] Assets(RecognitionKind kind) => kind switch
    {
        RecognitionKind.Document => Document,
        RecognitionKind.Table => Table,
        RecognitionKind.Formula => Formula,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private string Folder(RecognitionKind kind) => Path.Combine(directory, "recognition", kind switch
    {
        RecognitionKind.Document => "layout-cdla-1.2.0",
        RecognitionKind.Table => "slanet-plus-2.0.0",
        RecognitionKind.Formula => "pix2text-mfr-1.5",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    public bool IsInstalled(RecognitionKind kind) => Assets(kind).All(a => HasSize(Path.Combine(Folder(kind), a.Name), a.Size));
    public long DownloadSize(RecognitionKind kind) => Assets(kind).Sum(a => a.Size);

    public async Task<string[]> Prepare(RecognitionKind kind, bool download, IProgress<ModelDownload>? progress, CancellationToken token)
    {
        await installGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var assets = Assets(kind);
            string folder = Folder(kind);
            var paths = assets.Select(a => Path.Combine(folder, a.Name)).ToArray();
            long total = DownloadSize(kind), completed = 0;
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                string path = paths[i];
                if (!await Verified(path, asset, token).ConfigureAwait(false))
                {
                    if (!download)
                        throw new InvalidDataException("本机模型缺失或校验失败，请重新下载。");
                    Directory.CreateDirectory(folder);
                    await Download(asset, path, completed, total, progress, token).ConfigureAwait(false);
                }
                completed += asset.Size;
                progress?.Report(new(completed, total));
            }
            return paths;
        }
        finally
        {
            installGate.Release();
        }
    }

    private static bool HasSize(string path, long size)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == size;
    }

    private static async Task<bool> Verified(string path, Asset asset, CancellationToken token)
    {
        if (!HasSize(path, asset.Size))
            return false;
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
        return Convert.ToHexString(hash) == asset.Hash;
    }

    private static async Task Download(Asset asset, string path, long completed, long total, IProgress<ModelDownload>? progress, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            var buffer = new byte[128 * 1024];
            long received = 0;
            int reported = -1;
            await using (var output = File.Create(temporary))
            {
                int count;
                while ((count = await source.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) != 0)
                {
                    received += count;
                    if (received > asset.Size)
                        throw new InvalidDataException("模型文件大小与发布清单不符。");
                    await output.WriteAsync(buffer.AsMemory(0, count), limit.Token).ConfigureAwait(false);
                    int percent = (int)((completed + received) * 100 / total);
                    if (percent != reported)
                    {
                        progress?.Report(new(completed + received, total));
                        reported = percent;
                    }
                }
            }
            if (!await Verified(temporary, asset, limit.Token).ConfigureAwait(false))
                throw new InvalidDataException("模型下载校验失败，请重试。");
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
