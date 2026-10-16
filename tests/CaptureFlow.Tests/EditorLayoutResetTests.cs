using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;

public partial class EditorViewModelTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public Task ResetLayoutExitsMonitorFocusWithoutChangingProject(bool customGeometry) => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, _) = await InteractionFixture(); await using var owner = vm;
        if (customGeometry) await vm.SaveWorkspaceAsync(new(MediaWidth: 300, InspectorWidth: 320, MediaVisible: false));
        var project = vm.Project; var undo = vm.Session.Snapshot();
        vm.IsMonitorFocused = true;
        await vm.ResetEditorWorkspaceCommand.ExecuteAsync(null);
        Assert.False(vm.IsMonitorFocused);
        Assert.Equal(new EditorWorkspace(), vm.Workspace);
        Assert.Same(project, vm.Project);
        Assert.Equal(undo.Undo.Length, vm.Session.Snapshot().Undo.Length);
    });

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public Task ResetLayoutPersistsPanelVisibilityAndSplitterDefaultsForReopenedEditor(bool mediaVisible, bool inspectorVisible) => DesktopRecordingCommandTests.Sta(async () =>
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowLayoutReset", Guid.NewGuid().ToString("N"), "workspace.json");
        var workspaceStore = new JsonEditorWorkspaceStore(path);
        await workspaceStore.SaveAsync(new(350, 390, 1, 3, mediaVisible, inspectorVisible));
        await using (var first = new EditorViewModel(new Backend(), new Store(), workspaceStore: workspaceStore))
        {
            await first.LoadWorkspaceAsync(); Assert.Equal(mediaVisible, first.Workspace.MediaVisible); Assert.Equal(inspectorVisible, first.Workspace.InspectorVisible);
            first.IsMonitorFocused = true;
            await first.ResetEditorWorkspaceCommand.ExecuteAsync(null);
            Assert.False(first.IsMonitorFocused); Assert.Equal(new EditorWorkspace(), first.Workspace);
            Assert.True(File.Exists(path));
        }
        // A fresh VM and store read disk, not the first VM's in-memory state.
        await using var reopened = new EditorViewModel(new Backend(), new Store(), workspaceStore: new JsonEditorWorkspaceStore(path));
        await reopened.LoadWorkspaceAsync();
        Assert.Equal(new EditorWorkspace(), reopened.Workspace);
        Assert.True(reopened.Workspace.MediaVisible); Assert.True(reopened.Workspace.InspectorVisible);
        Assert.False(reopened.IsMonitorFocused);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    });
}
