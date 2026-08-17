namespace Playroma.JRPG.Documents.Saving;

public interface IAtomicFileReplacer
{
    ValueTask ReplaceAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed class AtomicFileReplacer : IAtomicFileReplacer
{
    public ValueTask ReplaceAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        if (OperatingSystem.IsWindows() && File.Exists(destinationPath))
        {
            string replacementBackup = destinationPath + ".playroma-replace-backup";
            try
            {
                File.Replace(temporaryPath, destinationPath, replacementBackup, ignoreMetadataErrors: true);
                File.Delete(replacementBackup);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            catch (IOException)
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
        }
        else
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }

        return ValueTask.CompletedTask;
    }
}
