namespace WinToolbox.Core;

/// <summary>
/// Two-language strings. Chinese is picked when the Windows display language is Chinese,
/// which covers both Simplified and Traditional; English is the fallback.
/// </summary>
internal static class Loc
{
    public static readonly bool Chinese = Detect();

    private static bool Detect()
    {
        try
        {
            // LANGID layout: low 10 bits are the primary language id, 0x04 = Chinese.
            return (Core.Native.GetUserDefaultUILanguage() & 0x3FF) == 0x04;
        }
        catch
        {
            return false;
        }
    }

    public static string T(string zh, string en) => Chinese ? zh : en;
}
