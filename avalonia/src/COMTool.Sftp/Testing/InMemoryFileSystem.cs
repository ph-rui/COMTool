namespace COMTool.Sftp.Testing;

/// <summary>
/// An in-memory file system used by the kernel tests and by the headless
/// app tests. It implements both sides of the SFTP drawer so a test can
/// build a remote tree and a local tree and compare them.
/// </summary>
/// <remarks>
/// The double mirrors the behaviour of the real SSH.NET client that the
/// transfer engine has to survive: listing a symbolic link to a directory
/// returns the children of the target, but the paths are rebuilt from the
/// requested path, and listing a symbolic link to a file fails the way the
/// server fails.
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
        HomeDirectory = SftpPath.Normalize(homeDirectory);
        AddDirectory(HomeDirectory);
    }

    public string HomeDirectory { get; }

    /// <summary>
    /// Adds a file with the given text content.
    /// </summary>
    public void AddFile(string path, string content)
    {
        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        var node = new Node
        {
            Path = normalized,
            Kind = NodeKind.File,
            Content = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty),
        };

        _nodes[normalized] = node;
        Link(normalized, SftpPath.GetParent(normalized));
    }

    /// <summary>
    /// Adds a directory.
    /// </summary>
    public void AddDirectory(string path)
    {
        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        if (!_nodes.ContainsKey(normalized))
        {
            _nodes[normalized] = new Node { Path = normalized, Kind = NodeKind.Directory };
        }

        Link(normalized, SftpPath.GetParent(normalized));
    }

    /// <summary>
    /// Adds a symbolic link. The target may be absolute or relative to the
    /// directory of the link.
    /// </summary>
    public void AddSymlink(string path, string targetPath)
    {
        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        _nodes[normalized] = new Node
        {
            Path = normalized,
            Kind = NodeKind.Symlink,
            Target = targetPath,
        };

        Link(normalized, SftpPath.GetParent(normalized));
    }

    /// <summary>
    /// Returns the content of a file, or null when there is no such file.
    /// </summary>
    public string? ReadFile(string path)
    {
        var normalized = SftpPath.Normalize(path);
        if (!_nodes.TryGetValue(normalized, out var node))
        {
            return null;
        }

        var resolved = Resolve(node);
        return resolved.Kind == NodeKind.File
            ? System.Text.Encoding.UTF8.GetString(resolved.Content)
            : null;
    }

    /// <summary>
    /// Returns whether a node exists at the path.
    /// </summary>
    public bool HasEntry(string path)
    {
        return _nodes.ContainsKey(SftpPath.Normalize(path));
    }

    public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requested = SftpPath.Normalize(path);
        var node = Resolve(RequireNode(requested));
        if (node.Kind != NodeKind.Directory)
        {
            throw new FileNotFoundException(
                $"The path '{requested}' is not a directory.",
                requested);
        }

        var entries = new List<SftpEntry>();
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
        var resolved = Resolve(RequireNode(normalized));
        if (resolved.Kind != NodeKind.File)
        {
            throw new FileNotFoundException(
                $"The path '{normalized}' is not a file.",
                normalized);
        }

        return Task.FromResult<Stream>(new MemoryStream(resolved.Content, writable: false));
    }

    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = SftpPath.Normalize(path);
        EnsureParentDirectory(normalized);
        if (!_nodes.TryGetValue(normalized, out var node))
        {
            node = new Node { Path = normalized, Kind = NodeKind.File };
            _nodes[normalized] = node;
            Link(normalized, SftpPath.GetParent(normalized));
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

        var source = SftpPath.Normalize(oldPath);
        var target = SftpPath.Normalize(newPath);
        RequireNode(source);

        var moved = new List<string> { source };
        foreach (var key in _nodes.Keys.ToList())
        {
            if (key.Length > source.Length && key.StartsWith(source + "/", StringComparison.Ordinal))
            {
                moved.Add(key);
            }
        }

        foreach (var key in moved.OrderByDescending(value => value.Length))
        {
            var current = _nodes[key];
            var newKey = target + key[source.Length..];
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

        Link(target, SftpPath.GetParent(target));
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = SftpPath.Normalize(path);
        var node = RequireNode(normalized);
        if (node.Kind == NodeKind.Directory)
        {
            throw new InvalidOperationException(
                $"The path '{normalized}' is a directory.");
        }

        _nodes.Remove(normalized);
        Unlink(normalized);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = SftpPath.Normalize(path);
        var node = RequireNode(normalized);
        if (node.Kind != NodeKind.Directory)
        {
            throw new InvalidOperationException(
                $"The path '{normalized}' is not a directory.");
        }

        var descendants = _nodes.Keys
            .Where(key => key.Length > normalized.Length
                && key.StartsWith(normalized + "/", StringComparison.Ordinal))
            .OrderByDescending(value => value.Length)
            .ToList();

        if (!recursive && descendants.Count > 0)
        {
            throw new InvalidOperationException(
                $"The directory '{normalized}' is not empty.");
        }

        foreach (var key in descendants)
        {
            _nodes.Remove(key);
            Unlink(key);
        }

        _nodes.Remove(normalized);
        Unlink(normalized);
        return Task.CompletedTask;
    }

    public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = RequireNode(SftpPath.Normalize(path));
        node.Mode = octalMode & 0b111_111_111;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_nodes.ContainsKey(SftpPath.Normalize(path)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Node RequireNode(string path)
    {
        if (!_nodes.TryGetValue(path, out var node))
        {
            throw new DirectoryNotFoundException($"The path '{path}' was not found.");
        }

        return node;
    }

    /// <summary>
    /// Follows a chain of symbolic links. A link that points at nothing
    /// resolves to itself.
    /// </summary>
    private Node Resolve(Node node)
    {
        var current = node;
        for (var hop = 0; hop < MaxSymlinkHops && current.Kind == NodeKind.Symlink; hop++)
        {
            var target = current.Target ?? string.Empty;
            var resolved = target.StartsWith('/')
                ? SftpPath.Normalize(target)
                : SftpPath.Normalize(SftpPath.Combine(SftpPath.GetParent(current.Path), target));

            if (!_nodes.TryGetValue(resolved, out var next))
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    private IReadOnlyList<string> ChildrenOf(string directoryPath)
    {
        return _children.TryGetValue(directoryPath, out var children)
            ? children.ToList()
            : Array.Empty<string>();
    }

    private void EnsureParentDirectory(string path)
    {
        var parent = SftpPath.GetParent(path);
        if (parent == path)
        {
            return;
        }

        AddDirectory(parent);
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
