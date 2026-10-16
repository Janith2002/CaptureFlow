using System.Text.Json;
using System.Xml.Linq;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class PremiumWorkspaceTests
{
    [Fact]
    public async Task CollapsedSourcesRoundTripAndOldWorkspaceDefaultsRemainCompatible()
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowPremiumTests", Guid.NewGuid().ToString("N"), "studio.json");
        var store = new JsonStudioPreferencesStore(path);
        await store.SaveAsync(new(ShowSources: false));
        Assert.False((await store.LoadAsync()).ShowSources);
        var legacy = JsonSerializer.Deserialize<StudioPreferences>("{\"SourcesWidth\":220,\"PropertiesWidth\":300,\"MixerHeight\":280}")!;
        legacy.Validate(); Assert.True(legacy.ShowSources);
    }

    [Theory]
    [InlineData("EditorPage.xaml", "PreviewCommand,SelectToolCommand,TrimToolCommand,SplitCommand,DeleteCommand,UndoCommand,RedoCommand,ImportCommand,NewCommand,OpenProjectCommand,SaveProjectCommand,AddClipCommand,CutCommand,CopyCommand,PasteCommand,RippleDeleteCommand,JoinCommand,ApplyClipCommand,ApplyTrackCommand,AddKeyframeCommand,UpdateKeyframeCommand,DeleteKeyframeCommand,ResetAnimationCommand,ApplyTransitionCommand,DeleteTransitionCommand,DetectSilenceCommand,RemoveSilenceCommand,ExportCommand,CancelCommand")]
    [InlineData("ScreenRecorderPage.xaml", "AddStudioSourceCommand,RemoveStudioSourceCommand,ApplyPresetCommand,SavePresetCommand,RenamePresetCommand,DeletePresetCommand,FitPreviewCommand,ActualPreviewSizeCommand,StartScreenPreviewCommand,StopScreenPreviewCommand")]
    [InlineData("StudioRecordingControls.xaml", "StartCommand,PauseCommand,ResumeCommand,StopCommand,CancelCommand")]
    public void RedesignRetainsEssentialCommandRoutes(string page, string commands)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CaptureFlow.slnx"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var doc = XDocument.Load(Path.Combine(folder!.FullName, "src", "CaptureFlow.Desktop", "Views", page));
        var bindings = doc.Descendants().Attributes("Command").Select(a => a.Value).ToArray();
        foreach (var command in commands.Split(','))
        {
            // Studio routes screen preview through the mode-aware toolbar, which
            // is exercised for screen and camera paths by WebcamStartAvailabilityTests.
            var route = page == "ScreenRecorderPage.xaml" ? command switch
            { "StartScreenPreviewCommand" => "StartStudioPreviewCommand", "StopScreenPreviewCommand" => "StopStudioPreviewCommand", _ => command } : command;
            Assert.Contains(bindings, b => b == "{Binding " + route + "}");
        }
        // A page-level ScrollViewer would restore infinite-height measurement and
        // recreate the old preview/timeline starvation despite intact commands.
        Assert.DoesNotContain(doc.Root!.Elements(), e => e.Name.LocalName == "ScrollViewer");
    }
}

public partial class EditorViewModelTests
{
    [Theory] [InlineData(56)] [InlineData(80)] [InlineData(120)]
    public Task ResizedTimelineRowsKeepCrossTrackHitTestingAndUndo(double height) => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm;
        timeline.TrackHeight = height;
        Assert.Equal(25 + height * 3, timeline.Height);
        var original = vm.Project;
        Assert.True(timeline.BeginInteraction(new(250, 45)));
        timeline.UpdateInteraction(new(250, 25 + height + 20)); timeline.CompleteInteraction();
        Assert.Equal(1, vm.SelectedClip!.TrackId);
        vm.UndoCommand.Execute(null); Assert.Same(original, vm.Project);
    });
}
