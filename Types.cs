namespace Userscript2Extension
{
    internal enum LogType
    {
        Normal,
        Success,
        Warning,
        Error,
        Debug,
    }

    internal class Header
    {
        internal Dictionary<string, IEnumerable<string>> Headers = new();
    }

    internal class Size
    {
        internal int Width { get; }
        internal int Height { get; }


        internal Size(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}
