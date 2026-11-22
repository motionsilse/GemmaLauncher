namespace GemmaLauncher.Core;

public sealed class PortableUpdate
{
    private readonly string backup;
    public string Executable { get; }

    private PortableUpdate(string executable)
    {
        Executable = Path.GetFullPath(executable);
        backup = Executable + ".catloader-previous";
    }

    public static PortableUpdate Replace(string executable, string verifiedDownload)
    {
        var update = new PortableUpdate(executable);
        var download = Path.GetFullPath(verifiedDownload);
        if (!string.Equals(Path.GetDirectoryName(update.Executable), Path.GetDirectoryName(download), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(download, update.Executable, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(download, update.backup, StringComparison.OrdinalIgnoreCase) || !File.Exists(download))
            throw new InvalidDataException(Localization.T("engine.install.pathOutsideData"));
        InstallationService.RejectReparsePoints(download);
        InstallationService.RejectReparsePoints(update.Executable);
        InstallationService.RejectReparsePoints(update.backup);

        // Windows permits renaming a running EXE. The next image takes over the same
        // path so shortcuts and the Windows startup entry still point to this app.
        File.Move(update.Executable, update.backup, overwrite: true);
        try { File.Move(download, update.Executable); }
        catch
        {
            update.Restore();
            throw;
        }
        return update;
    }

    public void Restore()
    {
        InstallationService.RejectReparsePoints(Executable);
        InstallationService.RejectReparsePoints(backup);
        File.Move(backup, Executable, overwrite: true);
    }
}
