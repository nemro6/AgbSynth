using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgbSynth.App.Project;

public sealed class ProjectFileTransaction
{
    private readonly Dictionary<string, byte[]> _writes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletions = new(StringComparer.OrdinalIgnoreCase);

    public void AddWrite(string path, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);

        string fullPath = Path.GetFullPath(path);
        if (!_writes.TryAdd(fullPath, data))
            throw new InvalidOperationException($"The save plan contains the same file more than once: {fullPath}");
        _deletions.Remove(fullPath);
    }

    public void AddDelete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!_writes.ContainsKey(fullPath))
            _deletions.Add(fullPath);
    }

    public void Commit()
    {
        string transactionId = Guid.NewGuid().ToString("N");
        var operations = new List<FileOperation>();
        bool committed = false;

        try
        {
            foreach ((string targetPath, byte[] data) in _writes)
            {
                string? directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string temporaryPath = $"{targetPath}.{transactionId}.tmp";
                File.WriteAllBytes(temporaryPath, data);
                operations.Add(new FileOperation(targetPath, temporaryPath, $"{targetPath}.{transactionId}.bak", deleteOnly: false));
            }

            operations.AddRange(_deletions
                .Where(File.Exists)
                .Select(path => new FileOperation(path, null, $"{path}.{transactionId}.bak", deleteOnly: true)));

            foreach (FileOperation operation in operations)
            {
                if (File.Exists(operation.TargetPath))
                {
                    File.Move(operation.TargetPath, operation.BackupPath);
                    operation.OriginalMoved = true;
                }

                if (!operation.DeleteOnly)
                {
                    File.Move(operation.TemporaryPath!, operation.TargetPath);
                    operation.NewFileCommitted = true;
                }
            }
            committed = true;
        }
        catch
        {
            RollBack(operations);
            throw;
        }
        finally
        {
            foreach (FileOperation operation in operations)
            {
                TryDelete(operation.TemporaryPath);
            }
        }

        if (!committed)
            return;

        foreach (FileOperation operation in operations)
            TryDelete(operation.BackupPath);
    }

    private static void RollBack(IEnumerable<FileOperation> operations)
    {
        foreach (FileOperation operation in operations.Reverse())
        {
            try
            {
                if (operation.NewFileCommitted && File.Exists(operation.TargetPath))
                    File.Delete(operation.TargetPath);
                if (operation.OriginalMoved && File.Exists(operation.BackupPath))
                    File.Move(operation.BackupPath, operation.TargetPath, overwrite: true);
            }
            catch
            {
                // Preserve the original exception and leave the backup available for manual recovery.
            }
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale transaction file is safer than losing the original save error or saved data.
        }
    }

    private sealed class FileOperation(string targetPath, string? temporaryPath, string backupPath, bool deleteOnly)
    {
        public string TargetPath { get; } = targetPath;
        public string? TemporaryPath { get; } = temporaryPath;
        public string BackupPath { get; } = backupPath;
        public bool DeleteOnly { get; } = deleteOnly;
        public bool OriginalMoved { get; set; }
        public bool NewFileCommitted { get; set; }
    }
}
