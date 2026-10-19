using System.Collections.Concurrent;

namespace COMTool.Core.Config;

/// <summary>
/// Per-process SSH password cache. Passwords live in memory only and are
/// keyed by the same stable key used by the SSH session store.
/// </summary>
public sealed class SshPasswordCache
{
    private readonly ConcurrentDictionary<string, string> _passwords =
        new(StringComparer.Ordinal);

    public void Set(string key, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            _passwords.TryRemove(key, out _);
            return;
        }

        _passwords[key] = password;
    }

    public bool TryGet(string key, out string password) =>
        _passwords.TryGetValue(key, out password!);

    public string? Get(string key) =>
        _passwords.TryGetValue(key, out var password) ? password : null;

    public bool Remove(string key) => _passwords.TryRemove(key, out _);

    public void Clear() => _passwords.Clear();
}
