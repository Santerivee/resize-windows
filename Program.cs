using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Reflection;


namespace ResizeWindows;


internal class Program
{

    #region extern related

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        int uFlags
    );

    /// <summary>
    /// uFlags for SetWindowPos<br/>
    /// Use with or operator |
    /// </summary>
    /// <remarks>
    /// https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
    /// </remarks>
    private enum SWP
    {
        NOMOVE = 0X2,
        NOSIZE = 1,
        NOZORDER = 0X4,
        SHOWWINDOW = 0x0040
    }

    /// <summary>
    /// hWndInsertAfter for SetWindowPos,<br/>
    /// You may also pass a WindowHandle to make current window be on top of the passed handle
    /// </summary>
    private enum WINDOW_Z
    {
        TOP = 0,
        BOTTOM = 1,
        TOPMOST = -1,
        NOTOPMOST = -2
    }

    #endregion



#if DEBUG
	private static readonly string ConfigPath = File.ReadAllLines(Path.Join(AppContext.BaseDirectory, "debugpaths.txt"))[0];
    private static readonly string LogPath = File.ReadAllLines(Path.Join(AppContext.BaseDirectory, "debugpaths.txt"))[1];
#else
    private static readonly string ConfigPath = Path.Join(AppContext.BaseDirectory, "config.json");
    private static readonly string LogPath = Path.Join(AppContext.BaseDirectory, "log.txt");
#endif


    static void Main()
    {
        
        Dictionary<string, AppSetting> data = new();

        #region read & parse config
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        }
        catch(FileNotFoundException ex)
        {
            WriteConsole(new($"Config file not found in '{ConfigPath}'\n\n{ex}", LogLevel.Fatal));
            Exit();
            return;
        }
        catch (JsonException ex)
        {
            WriteConsole(new($"Malformed json: {ex.Message}", LogLevel.Fatal));
            Exit();
            return;
        }

        PropertyInfo[] properties = typeof(AppSetting).GetProperties();

        // just could not find a way to deserialize this automatically
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            bool skip = false;
            var cur = new AppSetting();
            
            foreach (var property in properties)
            {
                try
                {
                    // all properties are int... surely this will not need a rewrite in the near future
                    // TODO: 
                    // make Repeat optional with a default value of 0
                    // make it possible to order windows
                    int propertyValue = entry.Value.GetProperty(property.Name).GetInt32(); 
                    property.SetValue(cur, propertyValue);
                }
                catch (KeyNotFoundException ex)
                {
                    WriteLog(new FormattedMessage($"Malformed json: Key not found: {entry.Name}.{property.Name}\n{ex}", LogLevel.Error));
                    skip = true; break;
                }
            }
            if (!skip) data.Add(entry.Name, cur);
        };
        document.Dispose();
        #endregion


        #region move windows
        foreach (var (name, setting) in data)
        {
            var proc = Process.GetProcessesByName(name).FirstOrDefault();
            if (proc is null)
            {
                WriteLog(new FormattedMessage($"Could not get process for '{name}'", LogLevel.Error));
                continue;
            }

            IntPtr handle;
            try
            {
                handle = proc.MainWindowHandle;
            }
            catch (InvalidOperationException ex) 
            {
                WriteLog(new FormattedMessage($"Could not get handle for '{name}'\n{ex.Message}", LogLevel.Error));
                continue;
            }

            if (handle == IntPtr.Zero)
            {
                WriteLog(new FormattedMessage($"Could not get handle for '{name}' (returned IntPtr.Zero)", LogLevel.Error));
                continue;
            }

            for (int i = 0; i <= setting.Repeat; i++)
            {
                IntPtr result = SetWindowPos(handle, (IntPtr)WINDOW_Z.TOP, setting.X, setting.Y, setting.Width, setting.Height, (int)(SWP.SHOWWINDOW));

                if (result == IntPtr.Zero)
                {
                    WriteLog(new FormattedMessage($"Failed to resize '{name}'\n{Marshal.GetLastWin32Error()}", LogLevel.Error));
                    break;
                }
            }
        }
        #endregion
    }

    #region logging and utils
    /// <summary>
    /// Writes params messages to log file
    /// </summary>
    private static void WriteLog(params FormattedMessage[] msgs)
    {
        File.AppendAllLines(LogPath, msgs.Select(msg => msg.ToString()));
    }

    /// <summary>
    /// Write a message to the console and wait for user to confirm continue/exit
    /// </summary>
    private static void WriteConsole(FormattedMessage msg, bool exitAfter = false)
    {
        Console.WriteLine(msg);
        Console.WriteLine();
        Console.WriteLine($"Press any key to {(exitAfter ? "exit" : "continue execution")}...");
        Console.ReadKey();
    }

    /// <summary>
    /// Exit app!
    /// </summary>
    private static void Exit(int exitCode = 1)
    {
        Environment.Exit(exitCode);
    }
    #endregion

}

// needs to be class and properties, not fields for setValue
/// <summary>
/// JSON config object
/// </summary>
class AppSetting
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Repeat { get; set; }
}

/// <summary>
/// Logger message
/// </summary>
readonly struct FormattedMessage
{
    public string Message { get; }
    public LogLevel Level { get; }


    public FormattedMessage(string message, LogLevel level = LogLevel.Info)
    {
        Message = message;
        Level = level;
    }

    /// <summary>
    /// Create a FormattedMessage from a string and a log level
    /// </summary>
    /// <remarks>
    /// - <br/>
    /// 0 = Info <br/>
    /// 1 = Warning <br/>
    /// 2 = Error
    /// </remarks>
    public FormattedMessage(string message, int logLevel)
    {
        Message = message;
        Level = (LogLevel)logLevel;
    }

    public override string ToString()
    {
        return $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}] [{Level}] {Message}";
    }
}

// should be inside FormattedMessage but im too lazy to type out FormattedMessage.LogLevel.value
/// <summary>
/// log severity for FormattedMessage
/// </summary>
enum LogLevel
{
    Info,
    Warning,
    Error,
    Fatal
}