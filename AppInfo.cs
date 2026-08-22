using System.Reflection;

namespace SmartDropZone
{
    /// <summary>Version of the running build, read from the assembly.</summary>
    public static class AppInfo
    {
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.2";
    }
}
