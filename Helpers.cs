using System.Diagnostics;

namespace Userscript2Extension
{
    internal static class Helpers
    {
        internal static bool TryResolveChromePath(out string ChromeExecutablePath)
        {
            var path = Microsoft.Win32.Registry.GetValue(@"HKEY_CLASSES_ROOT\ChromeHTML\shell\open\command", null, null) as string;
            if (path != null)
            {
                var split = path.Split('\"');
                path = split.Length >= 2 ? split[1] : null;

                ChromeExecutablePath = path;
                return true;
            }

            // TODO: Implement fallbacks to locating chrome.exe path automatically
            // Check environment variables? and/or call 'where chrome' in the terminal
            // Can also check paths if they exists 'C:\Program Files (x86)\Google\Chrome\Application' and 'C:\Program Files\Google\Chrome\Application'
            // Also check registry key path 'HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe'

            ChromeExecutablePath = "";
            return false;
        }

        internal static bool TryResolveFfmpegPath(out string FfmpegExecutablePath)
        {
            string FfmpegPath = "";
            bool HasFfmpeg = Path.IsPathRooted((FfmpegPath = ExecuteCommand("where ffmpeg")));
            if (HasFfmpeg)
            {
                FfmpegExecutablePath = FfmpegPath;
                return true;
            }

            FfmpegExecutablePath = "";
            return false;
        }

        internal static void Log(string Message, LogType MessageType = LogType.Normal, bool Indented = false)
        {
            string Prefix = "[*]";
            switch (MessageType)
            {
                case LogType.Normal:
                    Console.ResetColor();
                    break;
                case LogType.Success:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Prefix = "[✓]";
                    break;
                case LogType.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Prefix = "[-]";
                    break;
                case LogType.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Prefix = "[X]";
                    break;
                case LogType.Debug:
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Prefix = "[~]";
                    break;
            }

            Console.WriteLine($"{(Indented ? "\t" : string.Empty)}{Prefix} {Message}");

            if (MessageType != LogType.Normal) Console.ResetColor();
        }

        internal static Size GetPngSize(string Path)
        {
            var buff = new byte[32];
            using (var d = File.OpenRead(Path))
            {
                d.Read(buff, 0, 32);
            }
            const int wOff = 16;
            const int hOff = 20;
            var Width = BitConverter.ToInt32([buff[wOff + 3], buff[wOff + 2], buff[wOff + 1], buff[wOff + 0],], 0);
            var Height = BitConverter.ToInt32([buff[hOff + 3], buff[hOff + 2], buff[hOff + 1], buff[hOff + 0],], 0);
            return new Size(Width, Height);
        }

        internal static string ExecuteCommand(string Command)
        {
            Process p = new();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = @$"/C {Command}";
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Trim();
        }

    }
}
