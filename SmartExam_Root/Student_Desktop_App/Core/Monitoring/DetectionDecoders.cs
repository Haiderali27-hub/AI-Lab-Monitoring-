namespace Student_Desktop_App.Core.Monitoring;

/// <summary>An axis-aligned detection box in pixels of whatever image it was decoded for.</summary>
public readonly record struct DetectionBox(float X, float Y, float Width, float Height)
{
    public float Area => Math.Max(0, Width) * Math.Max(0, Height);

    public static float IoU(DetectionBox a, DetectionBox b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = a.Area + b.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    /// <summary>
    /// Greedy non-maximum suppression: keeps the highest-scoring box and drops every other box overlapping a kept one
    /// by more than <paramref name="iouThreshold"/>. Returns the kept indices, best first.
    /// </summary>
    public static List<int> Nms(IReadOnlyList<DetectionBox> boxes, IReadOnlyList<float> scores, float iouThreshold, int maxKeep = 50)
    {
        var order = Enumerable.Range(0, boxes.Count).OrderByDescending(i => scores[i]).ToList();
        var kept = new List<int>();
        foreach (var i in order)
        {
            if (kept.Count >= maxKeep) break;
            if (kept.All(k => IoU(boxes[k], boxes[i]) <= iouThreshold)) kept.Add(i);
        }
        return kept;
    }
}

/// <summary>
/// Module 5 (S8) — turns the raw outputs of OpenCV Zoo's YuNet face detector (<c>face_detection_yunet_2023mar.onnx</c>,
/// MIT) into faces. The network is anchor-free with three heads (strides 8, 16, 32); for every grid cell each head
/// gives a class score, an objectness score, a box (centre offset in cells, log width/height in cells) and five
/// landmarks (offsets in cells). Confidence is √(class × objectness). This is the same decoding OpenCV's own
/// FaceDetectorYN performs; OpenCvSharp 4.10 does not wrap that class, so the model is run through the DNN module
/// and decoded here, in pure code that is unit-tested with hand-made output tensors (M5-U-18).
/// </summary>
public static class YuNetDecoder
{
    public static readonly int[] Strides = { 8, 16, 32 };

    /// <summary>The network's twelve output names (OpenCV's <c>Net.Forward</c> fills them in this order).</summary>
    public static readonly string[] OutputNames = Strides.SelectMany(s => new[] { $"cls_{s}", $"obj_{s}", $"bbox_{s}", $"kps_{s}" }).ToArray();

    /// <summary>OpenCV's FaceDetectorYN default is 0.9; webcams in a lit lab score real faces 0.9+, posters and
    /// shadows mostly below 0.7, so 0.8 keeps faces under poor lighting without counting patterns as people.</summary>
    public const float DefaultScoreThreshold = 0.8f;
    public const float DefaultNmsThreshold = 0.3f;

    /// <summary>The input size must be a multiple of the largest stride (the frame is padded to it).</summary>
    public static int PadToStride(int size) => (size + 31) / 32 * 32;

    /// <param name="outputs">Output name → its values, flattened (cls/obj: one per cell, bbox: four, kps: ten).</param>
    /// <param name="inputWidth">Width of the (padded) network input.</param>
    /// <param name="inputHeight">Height of the (padded) network input.</param>
    /// <param name="contentWidth">Width of the real picture inside the input (before padding).</param>
    /// <param name="contentHeight">Height of the real picture inside the input: both axes are normalised to it (see <see cref="FacePoint"/>).</param>
    /// <exception cref="ArgumentException">An output is missing or has the wrong size (a different model file).</exception>
    public static IReadOnlyList<DetectedFace> Decode(IReadOnlyDictionary<string, float[]> outputs, int inputWidth, int inputHeight,
        int contentWidth, int contentHeight, float scoreThreshold = DefaultScoreThreshold, float nmsThreshold = DefaultNmsThreshold)
    {
        if (inputWidth % 32 != 0 || inputHeight % 32 != 0) throw new ArgumentException("YuNet input must be padded to a multiple of 32.");
        if (contentWidth <= 0 || contentHeight <= 0) throw new ArgumentException("Empty picture.");

        var boxes = new List<DetectionBox>();
        var scores = new List<float>();
        var landmarks = new List<FacePoint[]>();
        foreach (var stride in Strides)
        {
            int cols = inputWidth / stride, rows = inputHeight / stride, cells = cols * rows;
            var cls = Output(outputs, $"cls_{stride}", cells);
            var obj = Output(outputs, $"obj_{stride}", cells);
            var bbox = Output(outputs, $"bbox_{stride}", cells * 4);
            var kps = Output(outputs, $"kps_{stride}", cells * 10);

            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = r * cols + c;
                var score = MathF.Sqrt(Math.Clamp(cls[i], 0f, 1f) * Math.Clamp(obj[i], 0f, 1f));
                if (score < scoreThreshold) continue;

                var cx = (c + bbox[i * 4]) * stride;
                var cy = (r + bbox[i * 4 + 1]) * stride;
                var w = MathF.Exp(bbox[i * 4 + 2]) * stride;
                var h = MathF.Exp(bbox[i * 4 + 3]) * stride;
                boxes.Add(new DetectionBox(cx - w / 2, cy - h / 2, w, h));
                scores.Add(score);
                var points = new FacePoint[5];
                for (var n = 0; n < 5; n++)
                    points[n] = new FacePoint((kps[i * 10 + 2 * n] + c) * stride, (kps[i * 10 + 2 * n + 1] + r) * stride);
                landmarks.Add(points);
            }
        }

        return DetectionBox.Nms(boxes, scores, nmsThreshold)
            .Select(k =>
            {
                var b = boxes[k];
                float unit = contentHeight;
                return new DetectedFace(b.X / unit, b.Y / unit, b.Width / unit, b.Height / unit, scores[k],
                    landmarks[k].Select(p => new FacePoint(p.X / unit, p.Y / unit)).ToArray());
            })
            .ToList();
    }

    private static float[] Output(IReadOnlyDictionary<string, float[]> outputs, string name, int expected)
    {
        if (!outputs.TryGetValue(name, out var values)) throw new ArgumentException($"YuNet output '{name}' is missing.");
        if (values.Length != expected) throw new ArgumentException($"YuNet output '{name}' has {values.Length} values, expected {expected}.");
        return values;
    }
}

/// <summary>One object found by YOLOX, in pixels of the original frame.</summary>
public sealed record ObjectDetection(int ClassId, float Score, DetectionBox Box);

/// <summary>
/// Module 5 (S8) — decodes the raw output of Megvii's official YOLOX-Nano ONNX export (<c>yolox_nano.onnx</c>,
/// release 0.1.1rc0, Apache-2.0; 80 COCO classes). The export keeps the head's raw form: one row per anchor point
/// of the 8/16/32-stride grids (52²+26²+13² = 3549 rows for a 416×416 input), each
/// <c>[dx, dy, log w, log h, objectness, 80 class scores]</c> with objectness and classes already sigmoid-ed.
/// Box = ((cell + d)·stride, exp(log size)·stride), score = objectness × class. The frame was resized by
/// <c>ratio</c> into the top-left of the input (YOLOX's own letterbox), so dividing by it maps back to frame pixels.
/// Pure: unit-tested with a hand-made tensor (M5-U-18).
/// </summary>
public static class YoloxDecoder
{
    public const int InputSize = 416;
    public const int ClassCount = 80;
    public const int CocoPerson = 0;
    public const int CocoCellPhone = 67;
    public static readonly int[] Strides = { 8, 16, 32 };

    /// <summary>Rows the model produces for a square input of this size.</summary>
    public static int RowCount(int inputSize = InputSize) => Strides.Sum(s => (inputSize / s) * (inputSize / s));

    /// <param name="raw">The output tensor, flattened (rows × (5 + classes)).</param>
    /// <param name="ratio">Scale the frame was resized by before going into the input.</param>
    /// <param name="classFilter">Only these classes are decoded (null = all).</param>
    /// <exception cref="ArgumentException">The tensor does not match the expected shape (a different model file).</exception>
    public static IReadOnlyList<ObjectDetection> Decode(ReadOnlySpan<float> raw, float ratio, float scoreThreshold,
        IReadOnlyCollection<int>? classFilter = null, int inputSize = InputSize, int classCount = ClassCount, float nmsThreshold = 0.45f)
    {
        var width = 5 + classCount;
        var rows = RowCount(inputSize);
        if (raw.Length != rows * width) throw new ArgumentException($"YOLOX output has {raw.Length} values, expected {rows * width}.");
        if (ratio <= 0) throw new ArgumentException("Resize ratio must be positive.");

        var found = new List<ObjectDetection>();
        var row = 0;
        foreach (var stride in Strides)
        {
            var grid = inputSize / stride;
            for (var gy = 0; gy < grid; gy++)
            for (var gx = 0; gx < grid; gx++, row++)
            {
                var o = row * width;
                var objectness = raw[o + 4];
                if (objectness < scoreThreshold) continue;   // score = obj × cls ≤ obj
                var best = -1;
                var bestScore = 0f;
                for (var k = 0; k < classCount; k++)
                {
                    if (classFilter is not null && !classFilter.Contains(k)) continue;
                    var s = objectness * raw[o + 5 + k];
                    if (s > bestScore) { bestScore = s; best = k; }
                }
                if (best < 0 || bestScore < scoreThreshold) continue;

                var cx = (gx + raw[o]) * stride;
                var cy = (gy + raw[o + 1]) * stride;
                var w = MathF.Exp(raw[o + 2]) * stride;
                var h = MathF.Exp(raw[o + 3]) * stride;
                found.Add(new ObjectDetection(best, bestScore,
                    new DetectionBox((cx - w / 2) / ratio, (cy - h / 2) / ratio, w / ratio, h / ratio)));
            }
        }

        // Class-aware NMS: a phone held in front of a person must not suppress the person, or vice versa.
        return found.GroupBy(d => d.ClassId)
            .SelectMany(g =>
            {
                var list = g.ToList();
                return DetectionBox.Nms(list.Select(d => d.Box).ToList(), list.Select(d => d.Score).ToList(), nmsThreshold).Select(i => list[i]);
            })
            .OrderByDescending(d => d.Score)
            .ToList();
    }
}
