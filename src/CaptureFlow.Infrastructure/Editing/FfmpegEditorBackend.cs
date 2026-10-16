using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Infrastructure.Editing;

public sealed class FfmpegEditorBackend : IEditorMediaBackend, IEditorExportBackend
{
    private static string N(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    private static string ProbeExecutable(string ffmpeg) => FfmpegToolPaths.ProbeFor(ffmpeg);
    public async Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token)
    {
        path = Path.GetFullPath(path); var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        if (extension is not ("MP4" or "MP3" or "WAV")) throw new NotSupportedException("Import MP4, MP3 or WAV files.");
        using var process = new FfmpegProcess(ProbeExecutable(ffmpeg), ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,avg_frame_rate:stream_side_data=rotation", "-of", "json", path], false, true);
        using var reader = new StreamReader(process.Output); var jsonTask = reader.ReadToEndAsync(token);
        await CompleteAsync(process, jsonTask, TimeSpan.FromSeconds(30), token).ConfigureAwait(false); var json = await jsonTask.ConfigureAwait(false);
        if (json.Length > 128 * 1024) throw new IOException("Unexpectedly large media metadata.");
        using var doc = JsonDocument.Parse(json); var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var videos = extension == "MP4" ? streams.Where(s => s.GetProperty("codec_type").GetString() == "video").ToArray() : [];
        var video = videos.FirstOrDefault(); var width = videos.Length == 0 ? 0 : video.GetProperty("width").GetInt32(); var height = videos.Length == 0 ? 0 : video.GetProperty("height").GetInt32();
        if (videos.Length > 0 && video.TryGetProperty("side_data_list", out var side) && side.EnumerateArray().Any(s => s.TryGetProperty("rotation", out var r) && Math.Abs(r.GetDouble()) % 180 == 90)) (width, height) = (height, width);
        var duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var info = new FileInfo(path);
        var result = new EditorMedia(Guid.NewGuid(), path, extension, duration, width, height, videos.Length > 0, streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), info.Length, info.LastWriteTimeUtc.Ticks, FrameRate(video));
        (EditorProject.Empty with { Media = [result] }).Validate(); return result;
    }
    private static double FrameRate(JsonElement video)
    {
        if (video.ValueKind == JsonValueKind.Undefined || !video.TryGetProperty("avg_frame_rate", out var rate)) return 0;
        var parts = (rate.GetString() ?? "").Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? n / d : 0;
    }
    public async Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew(); double thumbTime = 0, filmTime = 0, waveTime = 0;
        string? thumbnail = null, filmstrip = null; var peaks = new float[512];
        if (media.HasVideo)
        {
            var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-artwork"); Directory.CreateDirectory(folder);
            thumbnail = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jpg");
            try { await FfmpegProcess.RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-n", "-ss", N(Math.Min(.1, media.Duration / 2)), "-i", media.Path, "-frames:v", "1", "-vf", "scale=240:135:force_original_aspect_ratio=decrease", thumbnail], TimeSpan.FromSeconds(30), token).ConfigureAwait(false); }
            catch { if (File.Exists(thumbnail)) File.Delete(thumbnail); throw; }
            thumbTime = watch.Elapsed.TotalMilliseconds; watch.Restart();
            filmstrip = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".jpg");
            try { await FfmpegProcess.RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-n", "-threads", "2", "-i", media.Path, "-frames:v", "1", "-vf", $"fps={N(8 / media.Duration)},scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2,tile=8x1", filmstrip], TimeSpan.FromHours(2), token).ConfigureAwait(false); }
            catch { if (File.Exists(thumbnail)) File.Delete(thumbnail); if (File.Exists(filmstrip)) File.Delete(filmstrip); throw; }
        }
        if (media.HasAudio)
        {
            filmTime = media.HasVideo ? watch.Elapsed.TotalMilliseconds : 0; watch.Restart();
            try
            {
            using var process = new FfmpegProcess(ffmpeg, ["-v", "error", "-nostdin", "-i", media.Path, "-vn", "-ac", "1", "-ar", "8000", "-f", "s16le", "pipe:1"], false, true);
            var read = ReadPeaksAsync(process.Output, peaks, media.Duration, token);
            await CompleteAsync(process, read, TimeSpan.FromHours(2), token).ConfigureAwait(false);
            }
            catch
            {
                if (thumbnail is not null && File.Exists(thumbnail)) File.Delete(thumbnail);
                if (filmstrip is not null && File.Exists(filmstrip)) File.Delete(filmstrip);
                throw;
            }
        }
        if (media.HasAudio) waveTime = watch.Elapsed.TotalMilliseconds;
        else if (media.HasVideo) filmTime = watch.Elapsed.TotalMilliseconds;
        return new(thumbnail, peaks, filmstrip, filmstrip is null ? 0 : 8, thumbTime, filmTime, waveTime);
    }
    private static async Task ReadPeaksAsync(Stream stream, float[] peaks, double duration, CancellationToken token)
    {
        var buffer = new byte[4096]; var sample = 0L; var carried = -1;
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (carried < 0) { carried = buffer[i]; continue; }
                var value = (short)(carried | buffer[i] << 8); carried = -1;
                var bucket = Math.Clamp((int)(sample++ / (duration * 8000) * peaks.Length), 0, peaks.Length - 1);
                peaks[bucket] = Math.Max(peaks[bucket], Math.Abs((int)value) / 32768f);
            }
        }
    }
    public async Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double thresholdDb, double minimumSeconds, string ffmpeg, CancellationToken token)
    {
        if (!media.HasAudio || !double.IsFinite(thresholdDb) || thresholdDb is < -80 or > -10 || !double.IsFinite(minimumSeconds) || minimumSeconds is < .1 or > 10) throw new ArgumentException("Silence threshold: -80 to -10 dB; minimum: 0.1 to 10 seconds.");
        var ranges = new List<EditorRange>(); double? start = null;
        using var process = new FfmpegProcess(ffmpeg, ["-v", "error", "-nostdin", "-i", media.Path, "-vn", "-af", $"silencedetect=noise={N(thresholdDb)}dB:d={N(minimumSeconds)},ametadata=mode=print:file=-", "-f", "null", "-"], false, true);
        var read = ReadAsync(); await CompleteAsync(process, read, TimeSpan.FromHours(2), token).ConfigureAwait(false);
        if (start is not null) ranges.Add(new(start.Value, media.Duration)); return ranges.ToArray();
        async Task ReadAsync()
        {
            using var reader = new StreamReader(process.Output);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("lavfi.silence_start=", StringComparison.Ordinal)) start = double.Parse(line.Split('=')[1], CultureInfo.InvariantCulture);
                if (line.StartsWith("lavfi.silence_end=", StringComparison.Ordinal) && start is not null)
                { if (ranges.Count >= 10000) throw new IOException("Too many silence intervals."); ranges.Add(new(start.Value, double.Parse(line.Split('=')[1], CultureInfo.InvariantCulture))); start = null; }
            }
        }
    }
    public async Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, CancellationToken token)
        => await RenderCoreAsync(project, settings, outputFolder, ffmpeg, progress, null, token).ConfigureAwait(false);
    public Task<string> RenderExportAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<EditorExportProgress> progress, CancellationToken token)
        => RenderCoreAsync(project, settings, outputFolder, ffmpeg, null, progress, token);
    private async Task<string> RenderCoreAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, IProgress<EditorExportProgress>? status, CancellationToken token)
    {
        // The render uses its recovery folder as CWD; preserve configured executable paths relative to the app's CWD.
        if (Path.GetDirectoryName(ffmpeg) is { Length: > 0 }) ffmpeg = Path.GetFullPath(ffmpeg);
        project.Validate(); settings.Validate(); token.ThrowIfCancellationRequested();
        project = project with { Clips = project.Clips.OrderBy(c => c.Start).ThenBy(c => c.TrackId).ToArray() };
        if (project.Clips.Length == 0) throw new ArgumentException("Add clips to the timeline before rendering.");
        if (settings.Format != "MP4" && !project.Clips.Any(c => project.Media.Single(m => m.Id == c.MediaId).HasAudio)) throw new ArgumentException("The timeline has no source audio. Choose MP4 for silent video.");
        foreach (var media in project.Media.Where(m => project.Clips.Any(c => c.MediaId == m.Id)))
        {
            var info = new FileInfo(media.Path);
            if (!info.Exists) throw new IOException("Source file is missing: " + media.Path);
            if (info.Length != media.Bytes || info.LastWriteTimeUtc.Ticks != media.ModifiedTicks) throw new IOException("Source file changed. Reimport it before rendering: " + media.Path);
        }
        outputFolder = Path.GetFullPath(outputFolder); Directory.CreateDirectory(outputFolder); FfmpegProcess.CheckStorage(outputFolder);
        var work = Path.Combine(outputFolder, ".edit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var final = Path.Combine(outputFolder, $"CaptureFlow_Edit_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.{settings.Format.ToLowerInvariant()}");
        var pending = Path.Combine(work, "render." + settings.Format.ToLowerInvariant());
        status?.Report(new("Preparing", final));
        // Failure/cancellation retain this uniquely owned recovery folder, never a misleading completed media file.
        await new JsonEditorProjectStore().SaveAsync(project, Path.Combine(work, "project.cfedit"), token).ConfigureAwait(false);
        var arguments = new List<string> { "-v", "error", "-nostdin", "-n", "-filter_complex_threads", "2" };
        foreach (var clip in project.Clips) arguments.AddRange(["-ss", N(clip.In - Preroll(clip) * clip.Speed), "-t", N(clip.Out - clip.In + Preroll(clip) * clip.Speed), "-threads", "2", "-i", project.Media.Single(m => m.Id == clip.MediaId).Path]);
        var graph = BuildGraph(project, settings, work);
        await File.WriteAllTextAsync(Path.Combine(work, "filters.txt"), graph, new UTF8Encoding(false), token).ConfigureAwait(false);
        arguments.AddRange(["-/filter_complex", "filters.txt"]);
        if (settings.Format == "MP4") arguments.AddRange(["-map", "[video]", "-c:v", "libx264", "-preset", "veryfast", "-crf", settings.Crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-movflags", "+faststart"]);
        arguments.AddRange(["-map", "[audio]", "-threads", "2"]);
        arguments.AddRange(settings.Format switch { "MP4" => new[] { "-c:a", "aac", "-b:a", "192k" }, "MP3" => ["-c:a", "libmp3lame", "-b:a", settings.Mp3Bitrate + "k"], _ => ["-c:a", "pcm_s16le"] });
        arguments.AddRange(["-t", N(project.Duration), "-progress", "pipe:1", pending]);
        try
        {
            using (var process = new FfmpegProcess(ffmpeg, arguments, false, true, work))
            {
                status?.Report(new("Exporting", final));
                var read = ReadProgressAsync(process.Output, project.Duration, progress, status, final, token);
                await CompleteAsync(process, read, TimeSpan.FromHours(24), token).ConfigureAwait(false);
            }
            status?.Report(new("Finalizing", final));
            var metadata = await ProbeAsync(pending, ffmpeg, token).ConfigureAwait(false);
            if (Math.Abs(metadata.Duration - project.Duration) > .15 || !metadata.HasAudio || settings.Format == "MP4" && (!metadata.HasVideo || metadata.Width != settings.Width || metadata.Height != settings.Height)) throw new IOException("Export validation found unexpected duration, dimensions or streams.");
            await FfmpegProcess.RunAsync(ffmpeg, ["-v", "error", "-nostdin", "-xerror", "-threads", "2", "-i", pending, "-map", "0", "-f", "null", "-"], TimeSpan.FromHours(24), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(pending, final, false); progress?.Report(1);
            // Retain the small reproducible project/filter/text/font manifest alongside the exported media.
            return final;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new IOException("Editor render failed. Sources are unchanged; recovery files retained at " + work + ". " + ex.Message, ex); }
    }
    private static async Task ReadProgressAsync(Stream output, double duration, IProgress<double>? progress, IProgress<EditorExportProgress>? status, string final, CancellationToken token)
    {
        using var reader = new StreamReader(output);
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
            {
                var fraction = EditorExportProgress.FractionFor(micros, duration);
                if (fraction is { } value) progress?.Report(value * .95);
                status?.Report(new("Exporting", final, fraction));
            }
    }
    private static async Task CompleteAsync(FfmpegProcess process, Task read, TimeSpan timeout, CancellationToken token)
    {
        var finish = process.FinishAsync(timeout, token);
        try
        {
            await Task.WhenAny(finish, read).ConfigureAwait(false);
            if (read.IsFaulted || read.IsCanceled) await read.ConfigureAwait(false);
            await finish.ConfigureAwait(false); await read.ConfigureAwait(false);
        }
        catch
        {
            process.Dispose();
            try { await finish.ConfigureAwait(false); } catch (Exception) { }
            try { await read.ConfigureAwait(false); } catch (Exception) { }
            throw;
        }
    }
    private static double Preroll(EditorClip clip) => (clip.Transitions ?? []).SingleOrDefault(t => t.Type == TransitionType.CrossDissolve)?.Duration ?? 0;
    private static string BuildGraph(EditorProject project, EditorExportSettings settings, string work)
    {
        var graph = new StringBuilder(); var audio = new List<string> { "[silence]" }; var duration = N(project.Duration);
        graph.Append($"anullsrc=r=48000:cl=stereo,atrim=duration={duration}[silence];");
        var last = "base";
        if (settings.Format == "MP4") graph.Append($"color=c=black:s={settings.Width}x{settings.Height}:r={settings.FramesPerSecond}:d={duration},format=yuv420p[base];");
        for (var i = 0; i < project.Clips.Length; i++)
        {
            var clip = project.Clips[i]; var media = project.Media.Single(m => m.Id == clip.MediaId); var track = project.Tracks.Single(t => t.Id == clip.TrackId);
            var preroll = Preroll(clip); var renderStart = clip.Start - preroll; var renderDuration = clip.Duration + preroll;
            var local = $"(t-{N(preroll)})";
            var outgoingDissolve = project.Clips.Where(c => c.TrackId == 0 && Math.Abs(c.Start - clip.End) < .00001).Select(Preroll).DefaultIfEmpty(0).Max();
            if (settings.Format == "MP4" && track.Video)
            {
                graph.Append($"[{i}:v:0]trim=duration={N(clip.Out - clip.In + preroll * clip.Speed)},setpts=(PTS-STARTPTS)/{N(clip.Speed)}");
                if (clip.Crop is { } crop) graph.Append($",crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1");
                graph.Append(clip.Rotation switch { 90 => ",transpose=1", 180 => ",hflip,vflip", 270 => ",transpose=2", _ => "" });
                if (clip.VideoScale > 1)
                {
                    var sourceWidth = clip.Crop?.Width ?? media.Width; var sourceHeight = clip.Crop?.Height ?? media.Height;
                    if (clip.Rotation is 90 or 270) (sourceWidth, sourceHeight) = (sourceHeight, sourceWidth);
                    var factor = Math.Min(settings.Width / (double)sourceWidth, settings.Height / (double)sourceHeight) * clip.VideoScale;
                    var cropWidth = Math.Max(2, (int)Math.Min(sourceWidth, settings.Width / factor) / 2 * 2);
                    var cropHeight = Math.Max(2, (int)Math.Min(sourceHeight, settings.Height / factor) / 2 * 2);
                    graph.Append($",crop={cropWidth}:{cropHeight}:(iw-ow)/2:(ih-oh)/2");
                }
                var scale = Math.Min(1, clip.VideoScale);
                var scaledWidth = Math.Max(2, (int)(settings.Width * scale) / 2 * 2); var scaledHeight = Math.Max(2, (int)(settings.Height * scale) / 2 * 2);
                graph.Append($",scale={scaledWidth}:{scaledHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2,crop=w='min(iw,{settings.Width})':h='min(ih,{settings.Height})',pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={settings.FramesPerSecond},format=yuv420p");
                if (clip.Text is { Content.Length: > 0 } text)
                {
                    var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
                    if (!File.Exists(font)) throw new IOException("Segoe UI font is unavailable for text rendering.");
                    if (!File.Exists(Path.Combine(work, "font.ttf"))) File.Copy(font, Path.Combine(work, "font.ttf"));
                    File.WriteAllText(Path.Combine(work, $"text-{i}.txt"), text.Content, new UTF8Encoding(false));
                    graph.Append($",drawtext=fontfile=font.ttf:textfile=text-{i}.txt:expansion=none:fontsize={Math.Max(1, (int)Math.Round(text.Size * settings.Height / 1080d))}:fontcolor=0x{text.Color[1..]}:x=(w-text_w)*{N(text.X)}:y=(h-text_h)*{N(text.Y)}");
                }
                foreach (var transition in clip.Transitions ?? [])
                    if (transition.Type != TransitionType.CrossDissolve)
                        graph.Append($",fade=t={(transition.Placement == TransitionPlacement.Start ? "in" : "out")}:st={N(transition.Placement == TransitionPlacement.Start ? preroll : renderDuration - transition.Duration)}:d={N(transition.Duration)}");
                if ((clip.Keyframes ?? []).Any(k => k.Property != AnimationProperty.Volume) || preroll > 0)
                {
                    var opacity = EditorAnimation.Expression(clip, AnimationProperty.Opacity, $"(T-{N(preroll)})");
                    if (preroll > 0) opacity = $"({opacity})*min(1,T/{N(preroll)})";
                    graph.Append($",format=yuva420p,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='255*({opacity})'");
                    if ((clip.Keyframes ?? []).Any(k => k.Property == AnimationProperty.Scale))
                    {
                        var zoom = EditorAnimation.Expression(clip, AnimationProperty.Scale, local);
                        graph.Append($",scale=w='max(2,trunc({settings.Width}*({zoom})/2)*2)':h='max(2,trunc({settings.Height}*({zoom})/2)*2)':eval=frame");
                    }
                    if ((clip.Keyframes ?? []).Any(k => k.Property == AnimationProperty.Rotation))
                        graph.Append($",rotate=angle='({EditorAnimation.Expression(clip, AnimationProperty.Rotation, local)})*PI/180':ow=iw:oh=ih:c=black@0");
                }
                if (clip.FadeIn > 0 && !(clip.Transitions ?? []).Any(t => t.Placement == TransitionPlacement.Start)) graph.Append($",fade=t=in:st={N(preroll)}:d={N(clip.FadeIn)}");
                if (clip.FadeOut > 0 && !(clip.Transitions ?? []).Any(t => t.Placement == TransitionPlacement.End)) graph.Append($",fade=t=out:st={N(renderDuration - clip.FadeOut)}:d={N(clip.FadeOut)}");
                var x = EditorAnimation.Expression(clip, AnimationProperty.PositionX, $"(t-{N(clip.Start)})");
                var y = EditorAnimation.Expression(clip, AnimationProperty.PositionY, $"(t-{N(clip.Start)})");
                graph.Append($",setpts=PTS+{N(renderStart)}/TB[v{i}];[{last}][v{i}]overlay=x='(W-w)/2+W*({x})':y='(H-h)/2+H*({y})':eof_action=pass:repeatlast=0:enable='gte(t,{N(renderStart)})*lt(t,{N(clip.End)})'[composite{i}];"); last = "composite" + i;
            }
            if (media.HasAudio)
            {
                graph.Append($"[{i}:a:0]atrim=duration={N(clip.Out - clip.In + preroll * clip.Speed)},asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo");
                var speed = clip.Speed;
                while (speed > 2) { graph.Append(",atempo=2"); speed /= 2; }
                while (speed < .5) { graph.Append(",atempo=0.5"); speed *= 2; }
                graph.Append($",atempo={N(speed)},apad,atrim=duration={N(renderDuration)},volume={N(clip.Muted || track.Muted ? 0 : clip.Volume * track.Volume)}");
                if ((clip.Keyframes ?? []).Any(k => k.Property == AnimationProperty.Volume))
                {
                    var gain = EditorAnimation.Expression(clip, AnimationProperty.Volume, local);
                    graph.Append($",aeval=exprs='val(0)*({gain})|val(1)*({gain})':channel_layout=stereo");
                }
                if (preroll > 0) graph.Append($",afade=t=in:st=0:d={N(preroll)}");
                if (outgoingDissolve > 0 && track.Video) graph.Append($",afade=t=out:st={N(renderDuration - outgoingDissolve)}:d={N(outgoingDissolve)}");
                if (clip.FadeIn > 0) graph.Append($",afade=t=in:st={N(preroll)}:d={N(clip.FadeIn)}");
                if (clip.FadeOut > 0) graph.Append($",afade=t=out:st={N(renderDuration - clip.FadeOut)}:d={N(clip.FadeOut)}");
                if (renderStart > 0)
                {
                    // Generate leading silence on demand instead of allocating adelay's potentially hours-long delay line.
                    graph.Append($"[trimmedAudio{i}];anullsrc=r=48000:cl=stereo,atrim=duration={N(renderStart)},asetpts=PTS-STARTPTS[leadingSilence{i}];[leadingSilence{i}][trimmedAudio{i}]concat=n=2:v=0:a=1[a{i}];");
                }
                else graph.Append($"[a{i}];");
                audio.Add($"[a{i}]");
            }
        }
        graph.Append(string.Join("", audio)).Append($"amix=inputs={audio.Count}:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.95:level=0:latency=1,atrim=duration={duration}[audio]");
        if (settings.Format == "MP4") graph.Append($";[{last}]null[video]");
        return graph.ToString();
    }
}

