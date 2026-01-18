using System;

namespace PinToDeck.Models
{
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public uint ProcessId { get; set; }
        public string? ProcessPath { get; set; }
        public string? Aumid { get; set; }
        public string? ClassName { get; set; }
    }
}
