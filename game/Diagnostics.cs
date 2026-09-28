using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// What's left behind when the game dies, for finding out why. Everything goes in the user
/// folder (user://, which on Windows is %APPDATA%\Hellwall; see SaveFolder):
///   diagnostics.log  a rolling trail: which screen, focus lost and regained, saves and loads,
///                    every engine error and C# exception with its stack, and every 10 seconds a
///                    breadcrumb (version, map, day, demons, memory, frame rate)
///   session.txt      "running" while the game is up, "closed" once it quits cleanly
///   crashes/         a report for each session that didn't close cleanly, written on the next
///                    launch: the trail and the tail of that session's Godot log
///   logs/            Godot's own logs, the last 20 sessions
/// A hard crash (the engine itself, or the graphics driver) can't run any code as it dies, so it's
/// found on the next launch by the session never having been marked closed. The main menu says so.
/// </summary>
public static partial class Diagnostics
{
    const string TrailPath = "user://diagnostics.log", SessionPath = "user://session.txt", CrashDir = "user://crashes";
    const int TrailLines = 400;

    static readonly Queue<string> Trail = new();
    static bool _started, _active;
    static string _screen = "starting";

    /// <summary>A report saved this launch for a session before it that didn't close cleanly: for the main menu to mention.</summary>
    public static string? LastCrashReport { get; private set; }

    /// <summary>What the game is showing, for the trail: "menu", "campaign", "editor", "game Plains seed 11".</summary>
    public static string Screen
    {
        get => _screen;
        set { if (_screen == value) return; _screen = value; Note($"screen: {value}"); }
    }

    /// <summary>Called once, first thing: files the last session if it died, and starts this one's trail.</summary>
    public static void Start(SceneTree tree)
    {
        if (_started) return;
        _started = true;
        if (DisplayServer.GetName() == "headless") return; // tests and tools: no session to watch, and the player's trail left alone
        _active = true;
        FileLastSession();
        Note($"start: Hellwall {Version} on {OS.GetName()} {OS.GetVersion()}, {OS.GetProcessorName()}, video {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}), screen {DisplayServer.ScreenGetSize()}");
        WriteSession("running");
        OS.AddLogger(new TrailLogger());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Note($"UNHANDLED C# EXCEPTION{(e.IsTerminating ? " (terminating)" : "")}: {e.ExceptionObject}"); Flush(); };
        TaskScheduler.UnobservedTaskException += (_, e) => Note($"unobserved task exception: {e.Exception}");
        tree.Root.CallDeferred(Node.MethodName.AddChild, new Watcher());
    }

    static string Version => (string)ProjectSettings.GetSetting("application/config/version", "dev");

    /// <summary>Add a line to the trail (timestamped), written out at once so a crash can't lose it.</summary>
    public static void Note(string line)
    {
        if (!_active) return;
        lock (Trail)
        {
            Trail.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}");
            while (Trail.Count > TrailLines) Trail.Dequeue();
        }
        Flush();
    }

    static void Flush()
    {
        string text;
        lock (Trail) text = string.Join("\n", Trail) + "\n";
        try { File.WriteAllText(ProjectSettings.GlobalizePath(TrailPath), text); } catch (Exception) { /* nowhere to report it */ }
    }

    static void WriteSession(string state)
    {
        try { File.WriteAllText(ProjectSettings.GlobalizePath(SessionPath), $"{state}\n{Version}\n{DateTime.Now:O}\n"); } catch (Exception) { }
    }

    /// <summary>The last session never said it closed: keep its trail and the end of its Godot log as a crash report.</summary>
    static void FileLastSession()
    {
        string session = ProjectSettings.GlobalizePath(SessionPath);
        if (!File.Exists(session) || !File.ReadAllText(session).StartsWith("running")) return;
        try
        {
            string dir = ProjectSettings.GlobalizePath(CrashDir);
            Directory.CreateDirectory(dir);
            string report = Path.Combine(dir, $"crash-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            string trail = ProjectSettings.GlobalizePath(TrailPath);
            // Godot has already moved the last session's godot.log aside, under its start time: the newest of those.
            string logs = ProjectSettings.GlobalizePath("user://logs");
            string? lastLog = Directory.Exists(logs) ? Directory.GetFiles(logs, "godot2*.log").OrderBy(f => f).LastOrDefault() : null;
            var lines = lastLog != null ? File.ReadAllLines(lastLog) : [];
            File.WriteAllText(report,
                $"Hellwall closed without shutting down: the session below never finished.\n\n{File.ReadAllText(session)}\n" +
                $"==== trail (diagnostics.log) ====\n{(File.Exists(trail) ? File.ReadAllText(trail) : "(none)")}\n" +
                $"==== end of its Godot log ({Path.GetFileName(lastLog ?? "none")}) ====\n{string.Join("\n", lines.TakeLast(200))}\n");
            LastCrashReport = report;
        }
        catch (Exception) { }
    }

    /// <summary>The folder the reports are in, opened in the file browser (Settings, and the main menu after a crash).</summary>
    public static void OpenFolder() => OS.ShellOpen(ProjectSettings.GlobalizePath("user://"));

    /// <summary>What the game is doing, for the breadcrumbs: set by Main while a game runs.</summary>
    public static Func<string>? Describe;

    /// <summary>Lives on the root, above every scene: breadcrumbs, focus, and marking a clean quit.</summary>
    sealed partial class Watcher : Node
    {
        double _clock;

        public override void _Process(double delta)
        {
            _clock += delta;
            if (_clock < 10) return;
            _clock = 0;
            string what = Describe?.Invoke() ?? "";
            Note($"breadcrumb: {Screen}{(what.Length > 0 ? " · " + what : "")} · {Engine.GetFramesPerSecond():0} fps · memory {OS.GetStaticMemoryUsage() / 1048576} MB · video {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0} MB · objects {Performance.GetMonitor(Performance.Monitor.ObjectCount):0}");
        }

        public override void _Notification(int what)
        {
            switch ((long)what)
            {
                case NotificationApplicationFocusOut: Note("focus lost (another window or screen)"); break;
                case NotificationApplicationFocusIn: Note("focus back"); break;
                case NotificationApplicationPaused: Note("paused by the OS"); break;
                case NotificationWMCloseRequest: Note("window closed"); break;
                case NotificationPredelete:
                    Note("quit cleanly");
                    WriteSession("closed");
                    break;
            }
        }
    }

    /// <summary>Every engine error and warning (C# exceptions in callbacks among them) onto the trail, with where it came from.</summary>
    sealed partial class TrailLogger : Logger
    {
        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            string kind = errorType switch { 1 => "WARNING", 2 => "SCRIPT ERROR", 3 => "SHADER ERROR", _ => "ERROR" };
            Note($"{kind}: {(rationale.Length > 0 ? rationale : code)}  ({function} at {file}:{line}){(rationale.Length > 0 && code.Length > 0 ? "\n    " + code : "")}");
        }

        public override void _LogMessage(string message, bool error)
        {
            if (error) Note("stderr: " + message.TrimEnd());
        }
    }
}
