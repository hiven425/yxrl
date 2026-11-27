using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Yxrl.Capture;

internal sealed record DocumentBlock(string Text, bool Heading = false, BitmapSource? Image = null);
internal sealed record TableCell(int Row, int Column, int Rows, int Columns, string Text);
internal sealed record RecognizedTable(int Rows, int Columns, IReadOnlyList<TableCell> Cells)
{
    public string ToTsv()
    {
        var grid = Enumerable.Range(0, Rows).Select(_ => new string?[Columns]).ToArray();
        foreach (var cell in Cells)
            grid[cell.Row][cell.Column] = cell.Text.Replace('\t', ' ').ReplaceLineEndings(" ");
        return string.Join(Environment.NewLine, grid.Select(row => string.Join('\t', row)));
    }
}

internal static class StructureRecognition
{
    private const int TableSide = 488;
    private const int LayoutWidth = 608;
    private const int LayoutHeight = 800;
    private static readonly float[] Mean = [.485f, .456f, .406f], Deviation = [.229f, .224f, .225f];
    private sealed record Region(string Kind, Rect Bounds, float Score);

    public static DocumentBlock[] ReadDocument(string model, BitmapSource image, OcrDocument text, CancellationToken token)
    {
        using var session = Open(model);
        var input = Pixels(image, LayoutWidth, LayoutHeight, LayoutWidth, LayoutHeight, rgb: false);
        using var output = Run(session, token, NamedOnnxValue.CreateFromTensor("image", input));
        var values = output.Select(o => o.AsTensor<float>()).ToArray();
        var labels = Labels(session);
        var regions = new List<Region>();
        for (int level = 0; level < 4; level++)
        {
            token.ThrowIfCancellationRequested();
            int stride = 8 << level, gridWidth = (LayoutWidth + stride - 1) / stride;
            var scores = values[level];
            var offsets = values[level + 4];
            for (int p = 0; p < scores.Dimensions[1]; p++)
            {
                int label = Best(scores, p);
                float score = scores[0, p, label];
                if (score < .42f)
                    continue;
                double x = (p % gridWidth + .5) * stride, y = (p / gridWidth + .5) * stride;
                double left = x - Distance(offsets, p, 0) * stride, right = x + Distance(offsets, p, 2) * stride;
                double top = y - Distance(offsets, p, 1) * stride, bottom = y + Distance(offsets, p, 3) * stride;
                var box = Box(left * image.PixelWidth / LayoutWidth, top * image.PixelHeight / LayoutHeight,
                    right * image.PixelWidth / LayoutWidth, bottom * image.PixelHeight / LayoutHeight, image);
                if (!box.IsEmpty)
                    regions.Add(new(labels[label], box, score));
            }
        }

        var kept = new List<Region>();
        foreach (var region in regions.OrderByDescending(r => r.Score))
            if (!kept.Any(other => other.Kind == region.Kind && Intersection(other.Bounds, region.Bounds) /
                (Area(other.Bounds) + Area(region.Bounds) - Intersection(other.Bounds, region.Bounds)) > .5))
                kept.Add(region);

        var blocks = new List<(Rect Bounds, DocumentBlock Block)>();
        var lines = kept.Select(_ => new List<OcrLine>()).ToArray();
        var unassigned = new List<OcrLine>();
        foreach (var line in text.Lines)
        {
            int closest = -1;
            double best = .3;
            for (int i = 0; i < kept.Count; i++)
            {
                if (kept[i].Kind == "figure")
                    continue;
                double share = Intersection(line.Bounds, kept[i].Bounds) / Math.Max(1, Area(line.Bounds));
                if (share <= best)
                    continue;
                best = share;
                closest = i;
            }
            if (closest < 0) unassigned.Add(line);
            else lines[closest].Add(line);
        }
        for (int i = 0; i < kept.Count; i++)
        {
            var region = kept[i];
            if (region.Kind == "figure")
                blocks.Add((region.Bounds, new("", Image: Crop(image, region.Bounds))));
            else if (lines[i].Count > 0)
                blocks.Add((region.Bounds, new(Paragraphs(text, lines[i]), region.Kind == "title")));
        }
        // Keep text from regions that the layout model did not classify.
        for (int first = 0; first < unassigned.Count;)
        {
            int end = first + 1;
            var bounds = unassigned[first].Bounds;
            while (end < unassigned.Count)
            {
                var prior = unassigned[end - 1].Bounds;
                var next = unassigned[end].Bounds;
                if (next.Top - prior.Bottom > Math.Max(prior.Height, next.Height) * .8 ||
                    Math.Abs(next.Left - prior.Left) > Math.Max(prior.Height, next.Height))
                    break;
                bounds.Union(next);
                end++;
            }
            blocks.Add((bounds, new(Paragraphs(text, unassigned.GetRange(first, end - first)))));
            first = end;
        }
        return blocks.OrderBy(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).Select(b => b.Block).ToArray();
    }

    public static RecognizedTable ReadTable(string model, BitmapSource image, OcrDocument text, CancellationToken token)
    {
        double scale = (double)TableSide / Math.Max(image.PixelWidth, image.PixelHeight);
        int width = Math.Max(1, (int)(image.PixelWidth * scale)), height = Math.Max(1, (int)(image.PixelHeight * scale));
        using var session = Open(model);
        using var output = Run(session, token, NamedOnnxValue.CreateFromTensor("x", Pixels(image, width, height, TableSide, TableSide, rgb: false)));
        var boxes = output.First().AsTensor<float>();
        var scores = output.Last().AsTensor<float>();
        string[] labels = ["", .. Labels(session), ""];
        var markup = new StringBuilder("<table>");
        var bounds = new List<Rect>();
        for (int p = 0; p < scores.Dimensions[1]; p++)
        {
            int id = Best(scores, p);
            if (id == labels.Length - 1)
                break;
            string label = labels[id];
            markup.Append(label);
            if (label is not ("<td" or "<td>" or "<td></td>"))
                continue;
            double[] x = [boxes[0, p, 0], boxes[0, p, 2], boxes[0, p, 4], boxes[0, p, 6]];
            double[] y = [boxes[0, p, 1], boxes[0, p, 3], boxes[0, p, 5], boxes[0, p, 7]];
            double factor = TableSide / scale;
            bounds.Add(Box(x.Min() * factor, y.Min() * factor, x.Max() * factor, y.Max() * factor, image));
        }
        XElement table;
        try { table = XElement.Parse(markup.Append("</table>").ToString()); }
        catch (XmlException ex) { throw new InvalidDataException("未识别出完整表格，请重新框选表头和表格边缘。", ex); }
        if (bounds.Count == 0)
            throw new InvalidDataException("选区没有识别出表格。");

        var contents = bounds.Select(_ => new List<OcrLine>()).ToArray();
        foreach (var line in text.Lines)
        {
            int cell = -1;
            double max = 0;
            for (int i = 0; i < bounds.Count; i++)
            {
                double overlap = Intersection(bounds[i], line.Bounds);
                if (overlap <= max) continue;
                cell = i;
                max = overlap;
            }
            if (cell >= 0)
                contents[cell].Add(line);
        }

        var cells = new List<TableCell>();
        var occupied = new HashSet<(int Row, int Col)>();
        int row = 0, columns = 0, position = 0;
        foreach (var tr in table.Descendants("tr"))
        {
            int column = 0;
            foreach (var td in tr.Elements("td"))
            {
                int rowspan = (int?)td.Attribute("rowspan") ?? 1;
                int colspan = (int?)td.Attribute("colspan") ?? 1;
                while (occupied.Contains((row, column))) column++;
                for (int r = row; r < row + rowspan; r++)
                    for (int c = column; c < column + colspan; c++)
                        if (!occupied.Add((r, c)))
                            throw new InvalidDataException("识别到重叠的合并单元格，请缩小选区后重试。");
                cells.Add(new(row, column, rowspan, colspan, string.Join(" ", contents[position++].Select(l => l.Text))));
                column += colspan;
                columns = Math.Max(columns, column);
            }
            row++;
        }
        if (cells.Count == 0 || cells.Count != bounds.Count)
            throw new InvalidDataException("未识别出完整表格，请重新框选。");
        int rows = Math.Max(row, cells.Max(c => c.Row + c.Rows));
        return new(rows, columns, cells);
    }

    public static string ReadFormula(string[] files, BitmapSource image, CancellationToken token)
    {
        const int side = 384;
        // The tokenizer uses ByteLevel BPE. Decoding needs only the fixed vocabulary and inverse byte alphabet.
        using var tokenizer = JsonDocument.Parse(File.ReadAllText(files[2]));
        var vocabulary = tokenizer.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()
            .ToDictionary(p => p.Value.GetInt32(), p => p.Name);
        var alphabet = new Dictionary<char, byte>();
        int extra = 256;
        for (int value = 0; value < 256; value++)
            alphabet[(char)(value is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 and <= 255 ? value : extra++)] = (byte)value;
        using var encoder = Open(files[0]);
        using var decoder = Open(files[1]);
        using var state = Run(encoder, token, NamedOnnxValue.CreateFromTensor("pixel_values", Pixels(image, side, side, side, side, rgb: true)));
        var hidden = NamedOnnxValue.CreateFromTensor("encoder_hidden_states", state.Single().AsTensor<float>());
        var ids = new List<long> { 1 };
        var bytes = new List<byte>();
        for (int n = 0; n < 384; n++)
        {
            using var result = Run(decoder, token, NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids.ToArray(), [1, ids.Count])), hidden);
            int id = Best(result.Single().AsTensor<float>(), ids.Count - 1);
            if (id == 2)
            {
                var latex = Encoding.UTF8.GetString(bytes.ToArray()).Trim();
                if (latex.Length == 0)
                    throw new InvalidDataException("选区没有识别出公式。");
                return latex;
            }
            if (id < 5 || !vocabulary.TryGetValue(id, out string? tokenText))
                throw new InvalidDataException("公式含无法识别的字符，请缩小选区或提高截图清晰度。");
            ids.Add(id);
            foreach (var letter in tokenText)
                bytes.Add(alphabet[letter]);
        }
        throw new InvalidDataException("公式过长，请分开框选后识别。");
    }

    private static string Paragraphs(OcrDocument text, List<OcrLine> lines) =>
        OcrLayout.Format(text with { Lines = lines }, new()).Text;

    private static InferenceSession Open(string path)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
        };
        return new InferenceSession(path, options);
    }

    private static IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(InferenceSession session, CancellationToken token,
        params NamedOnnxValue[] inputs)
    {
        token.ThrowIfCancellationRequested();
        using var options = new RunOptions();
        using var registration = token.Register(() => options.Terminate = true);
        try { return session.Run(inputs, session.OutputNames, options); }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
    }

    private static int Best(Tensor<float> scores, int index)
    {
        int best = 0;
        for (int i = 1; i < scores.Dimensions[2]; i++)
            if (scores[0, index, i] > scores[0, index, best]) best = i;
        return best;
    }

    private static double Distance(Tensor<float> offsets, int index, int side)
    {
        int first = side * 8;
        float largest = offsets[0, index, first];
        for (int i = 1; i < 8; i++) largest = Math.Max(largest, offsets[0, index, first + i]);
        double sum = 0, weighted = 0;
        for (int i = 0; i < 8; i++)
        {
            double weight = Math.Exp(offsets[0, index, first + i] - largest);
            sum += weight;
            weighted += i * weight;
        }
        return weighted / sum;
    }

    private static DenseTensor<float> Pixels(BitmapSource source, int width, int height, int targetWidth, int targetHeight, bool rgb)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        using var original = new SKBitmap(bgra.PixelWidth, bgra.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bgra.CopyPixels(Int32Rect.Empty, original.GetPixels(), original.ByteCount, original.RowBytes);
        using var scaled = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.White);
            using var sourceImage = SKImage.FromBitmap(original);
            canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), rgb
                ? new SKSamplingOptions(new SKCubicResampler(0, .5f))
                : new SKSamplingOptions(SKFilterMode.Linear));
        }
        var input = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);
        var pixels = scaled.GetPixelSpan();
        int plane = targetHeight * targetWidth;
        var values = input.Buffer.Span;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int start = y * scaled.RowBytes + x * 4, position = y * targetWidth + x;
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = pixels[start + (rgb ? 2 - channel : channel)] / 255f;
                    values[channel * plane + position] = rgb ? value * 2 - 1 : (value - Mean[channel]) / Deviation[channel];
                }
            }
        }
        return input;
    }

    private static string[] Labels(InferenceSession session) =>
        session.ModelMetadata.CustomMetadataMap["character"].Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    private static double Area(Rect box) => box.IsEmpty ? 0 : box.Width * box.Height;
    private static double Intersection(Rect first, Rect second) => Area(Rect.Intersect(first, second));
    private static Rect Box(double x, double y, double right, double bottom, BitmapSource source) =>
        Rect.Intersect(new Rect(new Point(x, y), new Point(right, bottom)), new Rect(0, 0, source.PixelWidth, source.PixelHeight));

    private static BitmapSource Crop(BitmapSource image, Rect bounds)
    {
        int left = (int)Math.Floor(bounds.Left), top = (int)Math.Floor(bounds.Top);
        var crop = new CroppedBitmap(image, new Int32Rect(left, top, (int)Math.Ceiling(bounds.Right) - left, (int)Math.Ceiling(bounds.Bottom) - top));
        crop.Freeze();
        return crop;
    }
}
