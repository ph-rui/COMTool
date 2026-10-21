using System.Text;

namespace COMTool.Sftp.Testing;

/// <summary>
/// An in-memory file system used by the kernel tests and by the headless
/// app tests. It implements both sides of the SFTP drawer so a test can
/// build a remote tree and a local tree and compare them.
/// </summary>
/// <remarks>
/// The double behaves like a real file system. A symbolic link is followed
/// in every part of a path, so the file behind "/tree/link/inner.txt" is
/// the file behind "/tree/real/inner.txt". Listing a directory that is
/// reached through a link returns the children of the target, but the
/// paths are rebuilt from the requested path, the way the server does it.
/// </remarks>
public sealed class InMemoryFileSystem : ISftpFileSystem, ILocalFileSystem
{
    private const int MaxSymlinkHops = 32;

    private enum NodeKind
    {
        File,
        Directory,
        Symlink,
    }

    private sealed class Node
    {
        public required string Path { get; init; }

        public NodeKind Kind { get; init; }

        public byte[] Content { get; set; } = Array.Empty<byte>();

        public string? Target { get; init; }

        public int Mode { get; set; } = 420;

        public DateTime LastWriteTimeUtc { get; set; } = DateTime.UtcNow;
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);

    public InMemoryFileSystem(string homeDirectory)
    {
        EnsureRoot();
        HomeDirectory = SftpPath.Normalize(homeDirectory);
        AddDirectory(HomeDirectory);
    }

    public string HomeDirectory { get; }

    /// <summary>
    /// When this is set, a listing also holds the two entries of the
    /// directory itself, the way the sftp server of OpenSSH sends them.
    /// </summary>
    public bool IncludeDotEntries { get; set; }

    /// <summary>
    /// Adds a file with the given text content.
    /// </summary>
    public void AddFile(string path, string content)
    {
        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        var parent = ResolveParent(normalized);
        var physical = Join(parent.Path, SftpPath.GetFileName(normalized));

        _nodes[physical] = new Node
        {
            Path = physical,
            Kind = NodeKind.File,
            Content = Encoding.UTF8.GetBytes(content ?? string.Empty),
        };

        Link(physical, parent.Path);
    }

    /// <summary>
    /// Adds a directory.
    /// </summary>
    public void AddDirectory(string path)
    {
        var normalized = SftpPath.Normalize(path);
        if (normalized == SftpPath.Root)
        {
            EnsureRoot();
            return;
        }

        EnsureParentDirectory(normalized);
        var parent = ResolveParent(normalized);
        var physical = Join(parent.Path, SftpPath.GetFileName(normalized));

        if (!_nodes.ContainsKey(physical))
        {
            _nodes[physical] = new Node { Path = physical, Kind = NodeKind.Directory };
        }

        Link(physical, parent.Path);
    }

    /// <summary>
    /// Adds a symbolic link. The target may be absolute or relative to the
    /// directory of the link.
    /// </summary>
    public void AddSymlink(string path, string targetPath)
    {
        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        var parent = ResolveParent(normalized);
        var physical = Join(parent.Path, SftpPath.GetFileName(normalized));

        _nodes[physical] = new Node
        {
            Path = physical,
            Kind = NodeKind.Symlink,
            Target = targetPath,
        };

        Link(physical, parent.Path);
    }

    /// <summary>
    /// Returns the content of a file, or null when there is no such file.
    /// </summary>
    public string? ReadFile(string path)
    {
        var node = ResolvePath(SftpPath.Normalize(path));
        return node is not null && node.Kind == NodeKind.File
            ? Encoding.UTF8.GetString(node.Content)
            : null;
    }

    /// <summary>
    /// Returns whether a node exists at the path.
    /// </summary>
    public bool HasEntry(string path)
    {
        return ResolvePath(SftpPath.Normalize(path)) is not null;
    }

    public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requested = SftpPath.Normalize(path);
        var node = ResolvePath(requested);
        if (node is null)
        {
            throw new DirectoryNotFoundException($"The path '{requested}' was not found.");
        }

        if (node.Kind != NodeKind.Directory)
        {
            throw new FileNotFoundException(
                $"The path '{requested}' is not a directory.",
                requested);
        }

        var entries = new List<SftpEntry>();
        if (IncludeDotEntries)
        {
            entries.Add(new SftpEntry(
                ".",
                SftpPath.Combine(requested, "."),
                true,
                false,
                0,
                DateTime.UtcNow,
                SftpPermission.FromOctal(493)));
            entries.Add(new SftpEntry(
                "..",
                SftpPath.Combine(requested, ".."),
                true,
                false,
                0,
                DateTime.UtcNow,
                SftpPermission.FromOctal(493)));
        }

        foreach (var childPath in ChildrenOf(node.Path))
        {
            var child = _nodes[childPath];
            var name = SftpPath.GetFileName(childPath);
            entries.Add(new SftpEntry(
                name,
                SftpPath.Combine(requested, name),
                child.Kind == NodeKind.Directory,
                child.Kind == NodeKind.Symlink,
                child.Kind == NodeKind.File ? child.Content.LongLength : 0,
                child.LastWriteTimeUtc,
                SftpPermission.FromOctal(child.Mode)));
        }

        return Task.FromResult<IReadOnlyList<SftpEntry>>(entries);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = SftpPath.Normalize(path);
        var node = ResolvePath(normalized);
        if (node is null || node.Kind != NodeKind.File)
        {
            throw new FileNotFoundException(
                $"The path '{normalized}' is not a file.",
                normalized);
        }

        return Task.FromResult<Stream>(new MemoryStream(node.Content, writable: false));
    }

    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        var parent = ResolveParent(normalized);
        var physical = Join(parent.Path, SftpPath.GetFileName(normalized));

        if (!_nodes.TryGetValue(physical, out var node))
        {
            node = new Node { Path = physical, Kind = NodeKind.File };
            _nodes[physical] = node;
            Link(physical, parent.Path);
        }

        return Task.FromResult<Stream>(new CommitStream(bytes => node.Content = bytes));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddDirectory(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = ResolvePath(SftpPath.Normalize(oldPath));
        if (source is null)
        {
            throw new FileNotFoundException(
                $"The path '{oldPath}' was not found.",
                oldPath);
        }

        var normalizedTarget = SftpPath.Normalize(newPath);
        EnsureParentDirectory(normalizedTarget);
        var newParent = ResolveParent(normalizedTarget);
        var physical = Join(newParent.Path, SftpPath.GetFileName(normalizedTarget));

        var sourcePath = source.Path;
        var moved = new List<string> { sourcePath };
        foreach (var key in _nodes.Keys.ToList())
        {
            if (key.Length > sourcePath.Length
                && key.StartsWith(sourcePath + "/", StringComparison.Ordinal))
            {
                moved.Add(key);
            }
        }

        foreach (var key in moved.OrderByDescending(value => value.Length))
        {
            var current = _nodes[key];
            var newKey = physical + key[sourcePath.Length..];
            _nodes.Remove(key);
            _nodes[newKey] = new Node
            {
                Path = newKey,
                Kind = current.Kind,
                Content = current.Content,
                Target = current.Target,
                Mode = current.Mode,
                LastWriteTimeUtc = current.LastWriteTimeUtc,
            };

            Unlink(key);
        }

        Link(physical, newParent.Path);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = ResolvePath(SftpPath.Normalize(path));
        if (node is null)
        {
            throw new FileNotFoundException($"The path '{path}' was not found.", path);
        }

        if (node.Kind == NodeKind.Directory)
        {
            throw new InvalidOperationException($"The path '{path}' is a directory.");
        }

        _nodes.Remove(node.Path);
        Unlink(node.Path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = ResolvePath(SftpPath.Normalize(path));
        if (node is null)
        {
            throw new DirectoryNotFoundException($"The path '{path}' was not found.");
        }

        if (node.Kind != NodeKind.Directory)
        {
            throw new InvalidOperationException($"The path '{path}' is not a directory.");
        }

        var directoryPath = node.Path;
        var descendants = _nodes.Keys
            .Where(key => key.Length > directoryPath.Length
                && key.StartsWith(directoryPath + "/", StringComparison.Ordinal))
            .OrderByDescending(value => value.Length)
            .ToList();

        if (!recursive && descendants.Count > 0)
        {
            throw new InvalidOperationException($"The directory '{path}' is not empty.");
        }

        foreach (var key in descendants)
        {
            _nodes.Remove(key);
            Unlink(key);
        }

        _nodes.Remove(directoryPath);
        Unlink(directoryPath);
        return Task.CompletedTask;
    }

    public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = ResolvePath(SftpPath.Normalize(path));
        if (node is null)
        {
            throw new FileNotFoundException($"The path '{path}' was not found.", path);
        }

        node.Mode = octalMode & 0b111_111_111;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolvePath(SftpPath.Normalize(path)) is not null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Resolves a path to the node it points at. Every component of the
    /// path is followed, the way a real file system follows a symbolic link
    /// that is part of a longer path.
    /// </summary>
    private Node? ResolvePath(string path, int hops = 0)
    {
        var current = string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = SftpPath.GetParent(current);
                continue;
            }

            var candidate = Join(current, segment);
            if (!_nodes.TryGetValue(candidate, out var node))
            {
                return null;
            }

            if (node.Kind != NodeKind.Symlink)
            {
                current = candidate;
                continue;
            }

            if (hops >= MaxSymlinkHops)
            {
                return null;
            }

            var target = node.Target ?? string.Empty;
            var resolved = target.StartsWith('/')
                ? SftpPath.Normalize(target)
                : SftpPath.Normalize(SftpPath.Combine(SftpPath.GetParent(candidate), target));

            var rest = segments.Skip(index + 1);
            var combined = rest.Any()
                ? resolved + "/" + string.Join("/", rest)
                : resolved;

            return ResolvePath(combined, hops + 1);
        }

        var last = current.Length == 0 ? SftpPath.Root : current;
        return _nodes.TryGetValue(last, out var result) ? result : null;
    }

    private Node ResolveParent(string path)
    {
        var parentPath = SftpPath.GetParent(path);
        var parent = ResolvePath(parentPath);
        if (parent is null || parent.Kind != NodeKind.Directory)
        {
            throw new DirectoryNotFoundException($"The path '{parentPath}' was not found.");
        }

        return parent;
    }

    private void EnsureRoot()
    {
        if (!_nodes.ContainsKey(SftpPath.Root))
        {
            _nodes[SftpPath.Root] = new Node { Path = SftpPath.Root, Kind = NodeKind.Directory };
        }
    }

    private void EnsureParentDirectory(string path)
    {
        var parent = SftpPath.GetParent(path);
        if (parent == path)
        {
            return;
        }

        if (ResolvePath(parent) is null)
        {
            AddDirectory(parent);
        }
    }

    private IReadOnlyList<string> ChildrenOf(string directoryPath)
    {
        return _children.TryGetValue(directoryPath, out var children)
            ? children.ToList()
            : Array.Empty<string>();
    }

    private static string Join(string directory, string name)
    {
        if (directory.Length == 0)
        {
            return "/" + name;
        }

        return directory.EndsWith('/') ? directory + name : directory + "/" + name;
    }

    private void Link(string path, string parent)
    {
        if (!_children.TryGetValue(parent, out var children))
        {
            children = new List<string>();
            _children[parent] = children;
        }

        if (!children.Contains(path))
        {
            children.Add(path);
        }
    }

    private void Unlink(string path)
    {
        var parent = SftpPath.GetParent(path);
        if (_children.TryGetValue(parent, out var children))
        {
            children.Remove(path);
        }
    }

    /// <summary>
    /// A memory stream that writes its content back into the tree when it
    /// is disposed.
    /// </summary>
    private sealed class CommitStream : MemoryStream
    {
        private readonly Action<byte[]> _commit;

        public CommitStream(Action<byte[]> commit)
        {
            _commit = commit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _commit(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
