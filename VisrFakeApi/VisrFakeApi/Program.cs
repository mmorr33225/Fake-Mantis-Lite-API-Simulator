using Microsoft.AspNetCore.Mvc;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<FakeApiService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FakeApiService>());

var app = builder.Build();

app.MapGet("/", () => Results.Text("Fake Mantis Lite API is running."));

app.MapGet("/api/live1sec", (FakeApiService fake) =>
{
    return Results.Json(fake.GetLatestLive());
});

app.MapGet("/api/history1sec", (
    FakeApiService fake,
    [FromQuery] DateTime from,
    [FromQuery] DateTime to,
    [FromQuery] int? dqiOnly) =>
{
    if (to < from)
    {
        return Results.BadRequest(new { error = "'to' must be greater than or equal to 'from'." });
    }

    var result = fake.GetHistory(from, to, dqiOnly == 1);
    return Results.Json(result);
});

app.MapGet("/api/image/latest.jpg", (FakeApiService fake) =>
{
    var bytes = fake.GetLatestJpegBytes();
    return Results.File(bytes, "image/jpeg");
});

app.MapPost("/api/simulate", (FakeApiService fake, SimulationRequest req) =>
{
    var requestedMode = req.mode?.Trim().ToLowerInvariant() ?? "normal";

    var allowedModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "normal",
        "missing_live_data",
        "frame_drop",
        "mixed_dqi"
    };

    if (!allowedModes.Contains(requestedMode))
    {
        return Results.BadRequest(new
        {
            error = "Invalid mode.",
            allowedModes = allowedModes.OrderBy(x => x).ToArray()
        });
    }

    fake.SetMode(requestedMode);

    return Results.Json(new
    {
        ok = true,
        mode = fake.GetMode()
    });
});

app.MapGet("/api/status", (FakeApiService fake) =>
{
    var latest = fake.GetLatestLive();

    return Results.Json(new
    {
        mode = fake.GetMode(),
        latestTs = latest.ts,
        historyPoints = fake.GetHistoryCount(),
        imageRateHz = fake.GetCurrentImageRateHz(),
        liveSuppressed = fake.IsLiveSuppressed()
    });
});

app.Run("http://0.0.0.0:8477");

public sealed class FakeApiService : BackgroundService
{
    private readonly object _lock = new();
    private readonly Random _rand = new();

    private Live1SecPoint _latest = new();
    private readonly List<Live1SecPoint> _history = new();

    private byte[] _latestJpeg = Array.Empty<byte>();

    private double _phase = 0;
    private int _imageFrame = 0;
    private string _mode = "normal";

    private int _liveTick = 0;
    private int _imageTick = 0;

    public Live1SecPoint GetLatestLive()
    {
        lock (_lock)
        {
            return Clone(_latest);
        }
    }

    public byte[] GetLatestJpegBytes()
    {
        lock (_lock)
        {
            return _latestJpeg.ToArray();
        }
    }

    public void SetMode(string mode)
    {
        lock (_lock)
        {
            _mode = string.IsNullOrWhiteSpace(mode) ? "normal" : mode.ToLowerInvariant();
        }
    }

    public string GetMode()
    {
        lock (_lock)
        {
            return _mode;
        }
    }

    public int GetHistoryCount()
    {
        lock (_lock)
        {
            return _history.Count;
        }
    }

    public int GetCurrentImageRateHz()
    {
        lock (_lock)
        {
            return _mode == "frame_drop" && IsFrameDropActive_NoLock() ? 2 : 6;
        }
    }

    public bool IsLiveSuppressed()
    {
        lock (_lock)
        {
            return _mode == "missing_live_data" && IsMissingLiveWindowActive_NoLock();
        }
    }

    public object GetHistory(DateTime from, DateTime to, bool dqiOnly)
    {
        List<Live1SecPoint> points;

        lock (_lock)
        {
            IEnumerable<Live1SecPoint> q = _history.Where(x => x.ts >= from && x.ts <= to);

            if (dqiOnly)
                q = q.Where(x => x.dqi == 1);

            points = q.Select(Clone).ToList();
        }

        return new
        {
            from,
            to,
            dqiOnly = dqiOnly ? 1 : 0,
            count = points.Count,
            points
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SeedHistory();

        var liveLoop = RunLiveLoop(stoppingToken);
        var imageLoop = RunImageLoop(stoppingToken);

        await Task.WhenAll(liveLoop, imageLoop);
    }

    private async Task RunLiveLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var point = GenerateNextLivePoint(DateTime.UtcNow);

            lock (_lock)
            {
                _liveTick++;

                // Always save history
                _history.Add(Clone(point));

                // Only update /api/live1sec if not in a suppressed window
                bool suppressLiveUpdate =
                    _mode == "missing_live_data" &&
                    IsMissingLiveWindowActive_NoLock();

                if (!suppressLiveUpdate)
                {
                    _latest = point;
                }

                // Keep last 24 hours of 1-second history
                if (_history.Count > 86400)
                {
                    _history.RemoveRange(0, _history.Count - 86400);
                }
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task RunImageLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var jpeg = GenerateFakeJpeg();

            int delayMs;
            lock (_lock)
            {
                _imageTick++;
                _latestJpeg = jpeg;

                bool frameDropActive =
               _mode == "frame_drop" &&
               IsFrameDropActive_NoLock();

                // During frame drop window randomly skip frames
                if (frameDropActive && _rand.NextDouble() < 0.75)
                {
                    // Skip updating the image this cycle (simulate dropped frame)
                }
                else
                {
                    _latestJpeg = jpeg;
                }

                delayMs = 167; // keep the same loop timing (~6 checks/sec)
            }

            await Task.Delay(delayMs, stoppingToken);
        }
    }

    private void SeedHistory()
    {
        var now = DateTime.UtcNow;

        // Seed last 15 minutes so history endpoint works right away
        for (int i = 900; i >= 1; i--)
        {
            var ts = now.AddSeconds(-i);
            var point = GenerateNextLivePoint(ts);

            _latest = point;
            _history.Add(Clone(point));
        }

        _latestJpeg = GenerateFakeJpeg();
    }

    private Live1SecPoint GenerateNextLivePoint(DateTime ts)
    {
        string mode;
        lock (_lock)
        {
            mode = _mode;
        }

        _phase += 0.12;

        bool empty = _rand.NextDouble() < 0.03;
        bool pilot = _rand.NextDouble() >= 0.05;

        int dqi;
        if (empty)
        {
            dqi = 0;
        }
        else if (mode == "mixed_dqi")
        {
            dqi = _rand.NextDouble() < 0.70 ? 1 : 0;
        }
        else
        {
            dqi = _rand.NextDouble() < 0.85 ? 1 : 0;
        }

        int cubes = empty ? 0 : ClampInt((int)Math.Round(3 + 3 * Math.Sin(_phase * 0.9) + Noise(1.2)), 0, 6);

        double nhvDil = empty ? 0 : Clamp(930 + 15 * Math.Sin(_phase * 0.6) + Noise(4), 880, 980);
        double nhvCz = empty ? 0 : Clamp(995 + 20 * Math.Cos(_phase * 0.5) + Noise(5), 930, 1050);
        double si = empty ? 0 : Clamp(0.75 + 0.08 * Math.Sin(_phase * 1.1) + Noise(0.02), 0.55, 0.95);
        double ci = empty ? 0 : Clamp(1.05 + 0.07 * Math.Cos(_phase * 0.8) + Noise(0.02), 0.90, 1.20);
        bool vis = !empty && _rand.NextDouble() < 0.08;
        double ff = empty ? 0 : Clamp(12.0 + 1.8 * Math.Sin(_phase * 0.7) + Noise(0.5), 8, 18);
        double hr = empty ? 0 : Clamp(0.054 + 0.004 * Math.Cos(_phase * 0.4) + Noise(0.001), 0.040, 0.065);
        double dre = empty ? 0 : Clamp(98.2 + 0.5 * Math.Sin(_phase * 0.5) + Noise(0.15), 96.5, 99.5);
        double maxT1 = empty ? 0 : Clamp(745 + 6 * Math.Sin(_phase * 1.2) + Noise(1.5), 730, 760);
        double maxT2 = empty ? 0 : Clamp(752 + 6 * Math.Cos(_phase * 1.0) + Noise(1.5), 736, 768);
        int fr = ClampInt((int)Math.Round(719 + Noise(2)), 715, 721);
        double sensorT = Clamp(27.3 + 0.8 * Math.Sin(_phase * 0.1) + Noise(0.15), 25, 31);
        int flamePx = empty ? 0 : ClampInt((int)Math.Round(780 + 35 * Math.Sin(_phase * 1.3) + Noise(12)), 680, 860);
        int edgePx = empty ? 0 : ClampInt((int)Math.Round(150 + 10 * Math.Cos(_phase * 0.9) + Noise(5)), 120, 185);
        int fs = empty ? 0 : ClampInt((int)Math.Round(91 + 3 * Math.Sin(_phase * 1.5) + Noise(1)), 85, 96);
        int intTime = 300;
        int cubePct = empty ? 0 : ClampInt((int)Math.Round((cubes / 6.0) * 100), 0, 100);

        return new Live1SecPoint
        {
            ts = DateTime.SpecifyKind(ts, DateTimeKind.Utc),
            empty = empty,
            dqi = dqi,
            cubes = cubes,
            nhvDil = Math.Round(nhvDil, 1),
            nhvCz = Math.Round(nhvCz, 1),
            si = Math.Round(si, 2),
            ci = Math.Round(ci, 2),
            vis = vis,
            ff = Math.Round(ff, 1),
            hr = Math.Round(hr, 3),
            pilot = pilot,
            dre = Math.Round(dre, 1),
            maxT1 = Math.Round(maxT1, 1),
            maxT2 = Math.Round(maxT2, 1),
            fr = fr,
            sensorT = Math.Round(sensorT, 1),
            flamePx = flamePx,
            edgePx = edgePx,
            fs = fs,
            intTime = intTime,
            cubePct = cubePct
        };
    }

    private bool IsMissingLiveWindowActive_NoLock()
    {
        // 12-second cycle:
        // 8 seconds normal visible live updates
        // 4 seconds where /api/live1sec appears frozen but history still records
        int cyclePos = _liveTick % 12;
        return cyclePos >= 8 && cyclePos <= 11;
    }

    private bool IsFrameDropActive_NoLock()
    {
        // 18-image cycle:
        // first part normal
        // last part slowed down
        int cyclePos = _imageTick % 18;
        return cyclePos >= 10 && cyclePos <= 17;
    }

    private byte[] GenerateFakeJpeg()
    {
        _imageFrame++;

        string letter = ((_imageFrame - 1) % 6) switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            4 => "E",
            _ => "F"
        };

        int width = 640;
        int height = 480;

        using var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Black);

        for (int i = 0; i < 8; i++)
        {
            float x = 50 + i * 65 + (float)(12 * Math.Sin((_imageFrame + i) * 0.3));
            float y = 100 + (float)(30 * Math.Cos((_imageFrame + i * 2) * 0.2));
            float r = 16 + (i % 3) * 5;

            using var brush = new SolidBrush(Color.OrangeRed);
            g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
        }

        float rectX = 220 + (float)(20 * Math.Sin(_imageFrame * 0.15));
        float rectY = 170 + (float)(15 * Math.Cos(_imageFrame * 0.12));
        using (var pen = new Pen(Color.Yellow, 3))
        {
            g.DrawRectangle(pen, rectX, rectY, 200, 120);
        }

        string line1 = $"Fake Frame {_imageFrame}";
        string line2 = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z";

        using var bigFont = new Font("Arial", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        using var smallFont = new Font("Arial", 24, FontStyle.Regular, GraphicsUnit.Pixel);
        using var whiteBrush = new SolidBrush(Color.White);

        var letterSize = g.MeasureString(letter, bigFont);
        float letterX = (width - letterSize.Width) / 2f;
        float letterY = (height - letterSize.Height) / 2f - 10;
        g.DrawString(letter, bigFont, whiteBrush, letterX, letterY);

        g.DrawString(line1, smallFont, whiteBrush, 20, 20);
        g.DrawString(line2, smallFont, whiteBrush, 20, 55);

        using var ms = new MemoryStream();

        var encoder = GetJpegEncoder();
        var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);

        if (encoder != null)
            bmp.Save(ms, encoder, encParams);
        else
            bmp.Save(ms, ImageFormat.Jpeg);

        return ms.ToArray();
    }

    private static ImageCodecInfo? GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    private double Noise(double amplitude)
    {
        return (_rand.NextDouble() * 2.0 - 1.0) * amplitude;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static Live1SecPoint Clone(Live1SecPoint x)
    {
        return new Live1SecPoint
        {
            ts = x.ts,
            empty = x.empty,
            dqi = x.dqi,
            cubes = x.cubes,
            nhvDil = x.nhvDil,
            nhvCz = x.nhvCz,
            si = x.si,
            ci = x.ci,
            vis = x.vis,
            ff = x.ff,
            hr = x.hr,
            pilot = x.pilot,
            dre = x.dre,
            maxT1 = x.maxT1,
            maxT2 = x.maxT2,
            fr = x.fr,
            sensorT = x.sensorT,
            flamePx = x.flamePx,
            edgePx = x.edgePx,
            fs = x.fs,
            intTime = x.intTime,
            cubePct = x.cubePct
        };
    }
}

public sealed class Live1SecPoint
{
    public DateTime ts { get; set; }
    public bool empty { get; set; }
    public int dqi { get; set; }
    public int cubes { get; set; }
    public double nhvDil { get; set; }
    public double nhvCz { get; set; }
    public double si { get; set; }
    public double ci { get; set; }
    public bool vis { get; set; }
    public double ff { get; set; }
    public double hr { get; set; }
    public bool pilot { get; set; }
    public double dre { get; set; }
    public double maxT1 { get; set; }
    public double maxT2 { get; set; }
    public int fr { get; set; }
    public double sensorT { get; set; }
    public int flamePx { get; set; }
    public int edgePx { get; set; }
    public int fs { get; set; }
    public int intTime { get; set; }
    public int cubePct { get; set; }
}

public sealed class SimulationRequest
{
    public string mode { get; set; } = "normal";
}