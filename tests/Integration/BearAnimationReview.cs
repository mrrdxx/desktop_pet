using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Emotion;
using Point = System.Windows.Point;

// Diagnostic host only. Loads the built production MainWindow, uses a generic
// Application (no real archive/tray/mutex), and exposes its own window in the
// taskbar so Computer Use can select it. No input injection occurs here.
internal static class BearAnimationReview
{
    private static readonly string Data = Environment.GetEnvironmentVariable("BEAR_REVIEW_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "review-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static MainWindow Pet = null!;
    private static AnimationPlayer Player = null!;
    private static string LastState = "";
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(Data);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Pet = new MainWindow { Title = "Bear Animation Review", ShowInTaskbar = true, Left = 420, Top = 300 };
        Player = Field<AnimationPlayer>("_animPlayer");
        var petImage = (System.Windows.Controls.Image)Pet.FindName("PetImage");
        DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Image.SourceProperty,
            typeof(System.Windows.Controls.Image)).AddValueChanged(petImage, (_, _) =>
            File.AppendAllText(Path.Combine(Data, "source-changes.jsonl"), JsonSerializer.Serialize(State()) + "\n"));
        app.MainWindow = Pet;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            try
            {
                ProcessCommand();
                // close can disconnect the Visual during this timer callback.
                if (PresentationSource.FromVisual(Pet) is not null) RecordState();
            }
            catch (Exception ex) { File.AppendAllText(Path.Combine(Data, "errors.txt"), ex + "\n"); }
        };
        Pet.Closed += (_, _) => timer.Stop();
        Pet.Show();
        timer.Start();
        app.Run();
    }

    private static T Field<T>(string name) => (T)typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Pet)!;
    private static void SetField(string name, object? value) => typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Pet, value);
    private static void Refresh() => typeof(MainWindow).GetMethod("UpdateFrame",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Pet, null);

    private static void ProcessCommand()
    {
        var path = Path.Combine(Data, "command.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        int id = root.GetProperty("id").GetInt32();
        string command = root.GetProperty("command").GetString()!;
        // Commands are acknowledged by id; leave their files as evidence.
        if (File.Exists(Path.Combine(Data, $"ack-{id}.txt"))) return;
        switch (command)
        {
            case "live":
                SetField("TestGetCursorPos", null);
                Pet.HoverDetector.Reset();
                Pet.ResumeAnimation(); Player.PlayAction("idle"); Refresh();
                break;
            case "play":
                // Park the logical input far away during manual expression review.
                // This mode proves rendering/playback, not real mouse detection.
                SetField("TestGetCursorPos", (Func<(int, int)>)(() => (-10000, -10000)));
                Pet.HoverDetector.Reset();
                Pet.ResumeAnimation(); Player.PlayAction(root.GetProperty("action").GetString()!); Refresh();
                break;
            case "pause": Pet.PauseAnimation(); break;
            case "resume": Pet.ResumeAnimation(); break;
            case "grievance":
                // Seed only the test host, not user data or the system date.
                var pending = new GrievanceExpressionState();
                pending.Restore(true, DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"));
                SetField("TestGrievanceExpression", pending);
                SetField("TestEmotionAccumulator", new EmotionDecayAccumulator(50, 85, DateTime.UtcNow));
                var near = Pet.PointToScreen(new Point(60, 80));
                SetField("TestGetCursorPos", (Func<(int, int)>)(() => ((int)near.X, (int)near.Y)));
                Pet.ResumeAnimation();
                break;
            case "capture": Capture(root.GetProperty("name").GetString()!); break;
            case "close": Pet.Close(); break;
            default: throw new InvalidOperationException("Unknown review command: " + command);
        }
        File.WriteAllText(Path.Combine(Data, $"ack-{id}.txt"), command);
    }

    private static object State()
    {
        var image = (System.Windows.Controls.Image)Pet.FindName("PetImage");
        var source = (BitmapSource)image.Source;
        var position = Pet.PointToScreen(new Point(0, 0));
        var frame = Player.GetCurrentFrame();
        return new {
            ms = Clock.ElapsedMilliseconds, action = Pet.CurrentAction, frame = frame.frame.ImagePath,
            offsetX = Pet.CurrentFrameOffset.x, offsetY = Pet.CurrentFrameOffset.y,
            left = Pet.Left, top = Pet.Top, screenX = position.X, screenY = position.Y,
            dpi = VisualTreeHelper.GetDpi(Pet).PixelsPerDip, paused = Pet.IsAppPaused,
            playerPaused = Pet.IsPlayerPaused, dragging = Pet.IsDragging, menu = Pet.IsContextMenuOpen,
            expressing = Field<bool>("_isExpressingGrievance"),
            width = source.PixelWidth, height = source.PixelHeight,
            assets = Field<string>("_assetDir")
        };
    }

    private static void RecordState()
    {
        var state = State();
        File.WriteAllText(Path.Combine(Data, "state.json"), JsonSerializer.Serialize(state, JsonOptions));
        // Log every observed render/state change with real elapsed wall time.
        var key = $"{Pet.CurrentAction}|{Field<string>("_lastDisplayedFrame")}|{Pet.Left}|{Pet.Top}|{Pet.IsAppPaused}|{Pet.IsDragging}|{Pet.IsContextMenuOpen}";
        if (key == LastState) return;
        LastState = key;
        File.AppendAllText(Path.Combine(Data, "timeline.jsonl"), JsonSerializer.Serialize(state) + "\n");
    }

    private static void Capture(string name)
    {
        if (name != Path.GetFileName(name)) throw new ArgumentException("Capture name must be a leaf name.");
        Pet.UpdateLayout();
        var canvas = (Canvas)Pet.FindName("PetCanvas");
        var bitmap = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Data, name + ".png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(Data, name + ".json"), JsonSerializer.Serialize(State(), JsonOptions));
    }
}
