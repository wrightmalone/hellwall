using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
#if !NO_STEAMWORKS
using Steamworks;
#endif

namespace Hellwall.Game;

/// <summary>
/// The one place the game talks to Steam (docs/plans/steam.md). Start() once at launch: if the game
/// was started by Steam (or with --steam-test, as Valve's test app, Spacewar, 480), Steam is up and
/// Running; otherwise (a playtest zip, a dev run, headless tests, the bot) nothing happens and the
/// game carries on exactly as without it. Everything that wants Steam asks Running first.
///
/// Valve's native library is found here rather than by .NET's own search, which doesn't look where
/// Godot puts things: beside the executable (Windows, Linux, inside the Mac app), in the Mac app's
/// Frameworks, or in game/lib/steamworks when run from the project.
///
/// Nothing Steam-shaped may stop the game starting. .NET loads Steamworks.NET when a method that
/// uses it is first compiled, before that method's own catch can act, and the x64-only build of it
/// won't load on an ARM machine. So every call into it is in the section at the end (small methods,
/// never inlined), and the public methods here catch whatever loading it throws. An ARM build (the
/// Apple-silicon half of the Mac app) is compiled without it (NO_STEAMWORKS), and says so.
/// </summary>
public static partial class Steam
{
    /// <summary>Until the game has its own App ID: Valve's public test app, only with --steam-test.</summary>
    const uint TestAppId = 480;

    static bool _started;

    /// <summary>Steam is up: the game was launched through it and SteamAPI started.</summary>
    public static bool Running { get; private set; }

    /// <summary>Why it isn't, or who's signed in: for the diagnostics trail.</summary>
    public static string Status { get; private set; } = "not started";

    /// <summary>The player's Steam name, when Steam is up.</summary>
    public static string? PlayerName
    {
        get
        {
            if (!Running) return null;
            try { return PersonaCore(); } catch (Exception) { return null; }
        }
    }

    public static void Start(SceneTree tree, bool test)
    {
        if (_started) return;
        _started = true;
        if (DisplayServer.GetName() == "headless") { Status = "headless: no Steam"; return; }
        if (test)
        {
            // Steam reads the App ID from here when the game wasn't launched through it.
            System.Environment.SetEnvironmentVariable("SteamAppId", TestAppId.ToString());
            System.Environment.SetEnvironmentVariable("SteamGameId", TestAppId.ToString());
        }
        try
        {
            FindLibrary();
            if (!IsSteamRunningCore()) { Status = "the Steam client isn't running"; return; }
            Running = InitCore();
            Status = Running ? $"up, as {PersonaCore()} (app {AppIdCore()})" : "not launched through Steam (no App ID)";
            if (Running) tree.Root.CallDeferred(Node.MethodName.AddChild, new Pump());
        }
        catch (Exception e)
        {
            Running = false;
            Status = $"Steam isn't available here ({Unavailable(e)})";
        }
    }

    /// <summary>Why Steam's libraries wouldn't load, in a line.</summary>
    static string Unavailable(Exception e) => e switch
    {
        PlatformNotSupportedException => e.Message,
        System.IO.FileLoadException or BadImageFormatException => $"this build of Steamworks.NET doesn't run on {RuntimeInformation.ProcessArchitecture}",
        _ => $"{e.GetType().Name}: {e.Message}",
    };

    /// <summary>Steam's library for this platform: its file name, and the folders it may be in, likeliest first.</summary>
    static IEnumerable<string> Candidates()
    {
        string file = OS.GetName() switch { "Windows" => "steam_api64.dll", "macOS" => "libsteam_api.dylib", _ => "libsteam_api.so" };
        string exe = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        yield return System.IO.Path.Combine(exe, file);
        yield return System.IO.Path.Combine(exe, "..", "Frameworks", file); // a Mac app bundle
        string platform = OS.GetName() == "Windows" ? "windows-x64" : "osx-linux-x64";
        string project = ProjectSettings.GlobalizePath($"res://lib/steamworks/{platform}/{file}");
        if (project.Length > 0) yield return project; // run from the project
    }

    static bool _resolver;

    /// <summary>Point Steamworks.NET's calls at Valve's library wherever this build keeps it.</summary>
    static void FindLibrary()
    {
        if (_resolver) return;
        _resolver = true;
        NativeLibrary.SetDllImportResolver(SteamAssemblyCore(), (name, assembly, path) =>
        {
            if (!name.Contains("steam_api")) return IntPtr.Zero;
            foreach (var candidate in Candidates())
                if (System.IO.File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle)) return handle;
            return IntPtr.Zero; // let .NET look where it usually would
        });
    }

    /// <summary>An achievement earned (its API name): kept by Steam until StoreStats sends it. Nothing without Steam.</summary>
    public static void Unlock(string id)
    {
        if (!Running) return;
        try { UnlockCore(id); } catch (Exception) { }
    }

    /// <summary>A stat's value (a progress achievement's), sent with StoreStats.</summary>
    public static void SetStat(string name, int value)
    {
        if (!Running) return;
        try { SetStatCore(name, value); } catch (Exception) { }
    }

    /// <summary>Send what's been set to Steam (it shows the unlock, and keeps it).</summary>
    public static void StoreStats()
    {
        if (!Running) return;
        try { StoreCore(); } catch (Exception) { }
    }

    /// <summary>
    /// --selftest=steam: Steam can never stop the game starting. Where Steamworks.NET is in the build
    /// and loads, Valve's library is found and called (with no Steam client, as on a test machine, it
    /// says so); where it isn't or can't load (an ARM build, or an x64 one run on ARM), the game says
    /// why and carries on. Either passes: the game is fine. Doesn't start Steam.
    /// </summary>
    public static bool SelfTest()
    {
        string where = Candidates().FirstOrDefault(System.IO.File.Exists) ?? "(not found)";
        try
        {
            FindLibrary();
            bool running = IsSteamRunningCore(); // a real call into Valve's library: it has to have loaded
            GD.Print($"hellwall-selftest: PASS steam (library loaded from {where}; Steam client {(running ? "running" : "not running")})");
            return true;
        }
        catch (Exception e) when (e is System.IO.FileLoadException or BadImageFormatException or PlatformNotSupportedException)
        {
            GD.Print($"hellwall-selftest: PASS steam (not on this machine, and the game carries on: {Unavailable(e)})");
            return true;
        }
        catch (Exception e)
        {
            GD.Print($"hellwall-selftest: FAIL steam (library {where}: {e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    /// <summary>Lives on the root while Steam is up: its callbacks each frame, and a clean shutdown.</summary>
    sealed partial class Pump : Node
    {
        public override void _Process(double delta) => CallbacksCore();

        public override void _Notification(int what)
        {
            if (what != NotificationPredelete || !Running) return;
            Running = false;
            ShutdownCore();
        }
    }

    // Every call into Steamworks.NET, and nothing else: small, never inlined, so a build or a machine
    // without it fails here, where the callers above catch it.
#if NO_STEAMWORKS
    static PlatformNotSupportedException Missing() => new("this build of the game has no Steam (an ARM build: Steamworks.NET is x64 only)");
    static System.Reflection.Assembly SteamAssemblyCore() => throw Missing();
    static bool IsSteamRunningCore() => throw Missing();
    static bool InitCore() => throw Missing();
    static string PersonaCore() => throw Missing();
    static uint AppIdCore() => throw Missing();
    static void CallbacksCore() { }
    static void ShutdownCore() { }
    static void UnlockCore(string id) { }
    static void SetStatCore(string name, int value) { }
    static void StoreCore() { }
#else
    [MethodImpl(MethodImplOptions.NoInlining)] static System.Reflection.Assembly SteamAssemblyCore() => typeof(SteamAPI).Assembly;
    [MethodImpl(MethodImplOptions.NoInlining)] static bool IsSteamRunningCore() => SteamAPI.IsSteamRunning();
    [MethodImpl(MethodImplOptions.NoInlining)] static bool InitCore() => SteamAPI.Init();
    [MethodImpl(MethodImplOptions.NoInlining)] static string PersonaCore() => SteamFriends.GetPersonaName();
    [MethodImpl(MethodImplOptions.NoInlining)] static uint AppIdCore() => SteamUtils.GetAppID().m_AppId;
    [MethodImpl(MethodImplOptions.NoInlining)] static void CallbacksCore() => SteamAPI.RunCallbacks();
    [MethodImpl(MethodImplOptions.NoInlining)] static void ShutdownCore() => SteamAPI.Shutdown();
    [MethodImpl(MethodImplOptions.NoInlining)] static void UnlockCore(string id) => SteamUserStats.SetAchievement(id);
    [MethodImpl(MethodImplOptions.NoInlining)] static void SetStatCore(string name, int value) => SteamUserStats.SetStat(name, value);
    [MethodImpl(MethodImplOptions.NoInlining)] static void StoreCore() => SteamUserStats.StoreStats();
#endif
}
