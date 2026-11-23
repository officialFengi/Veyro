using System;
using System.IO;
using System.Threading;

namespace Veyro;

internal static class UpdateCache
{
    internal static readonly string DirectoryPath = Path.Combine(Path.GetDirectoryName(Preferences.FilePath)!, "updates");
    internal static string Setup => Path.Combine(DirectoryPath, "Veyro-Setup.exe");
    internal static string Partial => Path.Combine(DirectoryPath, "download.part");
    internal const string MutexName = @"Local\Veyro.UpdateDownload";

    internal static bool Acquire(Mutex mutex)
    {
        try { return mutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    internal static void CleanWhenIdle()
    {
        if (!Directory.Exists(DirectoryPath)) return;
        using var mutex = new Mutex(false, MutexName);
        if (!Acquire(mutex)) return;
        try { Delete(Partial); Delete(Setup); }
        finally { mutex.ReleaseMutex(); }
    }

    internal static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
