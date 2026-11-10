using System.Net.Sockets;
using GemmaLauncher.App;
using GemmaLauncher.Core;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestRecoveryRoutingAsync()
    {
        await using (var importing = new Fixture())
        {
            var vm = importing.ViewModel; await UntilAsync(() => !vm.IsBusy);
            await vm.ConnectModelFileAsync(Path.Combine(importing.Paths.Root, "invalid.json"));
            Assert(vm.HasError && vm.PrimaryLabel == GemmaLauncher.Core.Localization.T("ui.action.start"), "A failed import must not turn the server button into a retry of a different action.");
            Assert(vm.StatusTitle != GemmaLauncher.Core.Localization.T("ui.status.failedTitle"), "A failed import must not claim that the server failed to start.");
        }
        var cases = new (Exception Error, FailureRecovery Recovery)[]
        {
            (new IOException("network disconnected"), FailureRecovery.Retry),
            (new AnisAccessRequiredException(), FailureRecovery.Access),
            (new SocketException((int)SocketError.AddressAlreadyInUse), FailureRecovery.VerifyPort),
            (new InvalidDataException("integrity mismatch"), FailureRecovery.Redownload),
            (new ModelLoadMemoryException(new IOException("out of memory")), FailureRecovery.LoadSettings),
            (new IOException("disk full", unchecked((int)0x80070070)), FailureRecovery.Storage)
        };
        foreach (var (error, recovery) in cases)
        {
            await using var fixture = new Fixture(); var vm = fixture.ViewModel; await UntilAsync(() => !vm.IsBusy);
            fixture.Installer.NextRuntimeFailure = error; vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy);
            Assert(vm.HasError && !vm.IsServerRunning && vm.CanSelectModel && vm.Recovery == recovery, "Startup failure must expose the matching recovery without leaving a server: " + recovery);
            Assert(!vm.PrimaryLabel.StartsWith("ui."), "Recovery actions must resolve their localization keys.");
            var settingsRequested = false; var storageRequested = false;
            vm.LoadSettingsRequested += (_, _) => settingsRequested = true;
            vm.StorageRecoveryRequested += (_, _) => storageRequested = true;
            vm.PrimaryCommand.Execute(null);
            if (recovery == FailureRecovery.LoadSettings) Assert(settingsRequested && !vm.IsBusy && !vm.IsServerRunning, "Memory recovery must let users adjust settings before restarting.");
            else if (recovery == FailureRecovery.Storage) Assert(storageRequested && !vm.IsBusy && !vm.IsServerRunning, "Storage recovery must wait for the user instead of immediately repeating the failed download.");
            else { await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy); vm.PrimaryCommand.Execute(null); await UntilAsync(() => !vm.IsBusy); }
        }
    }
}
