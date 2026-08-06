using SoundDeck.Core;
using Microsoft.Win32;

namespace SoundDeck.Infrastructure;

public sealed class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SoundDeck";

    public Task<bool> IsEnabledAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return Task.FromResult(key?.GetValue(ValueName) is string);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("No se pudo determinar la ruta del ejecutable.");
                key.SetValue(ValueName, $"\"{executable}\" --background");
                return Task.FromResult(true);
            }
            key.DeleteValue(ValueName, false);
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
