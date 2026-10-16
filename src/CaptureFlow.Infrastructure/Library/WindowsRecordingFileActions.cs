using CaptureFlow.Core.Library;
using System.Runtime.InteropServices;
namespace CaptureFlow.Infrastructure.Library;
public sealed class WindowsRecordingFileActions : IRecordingFileActions
{
    public void Rename(string oldPath, string newPath) => File.Move(oldPath, newPath, overwrite: false);
    public Task RecycleAsync(string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? operation = null; IShellItem? item = null; Exception? failure = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveType != DriveType.Fixed)
                    throw new IOException("Recycle Bin actions require a local fixed drive. Manage this file in Explorer.");
                operation = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!);
                var files = (IFileOperation)operation!;
                // Force recycling, suppress extra dialogs/elevation, and fail instead of silently skipping.
                files.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x0004 | 0x0010 | 0x0400);
                var iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, in iid, out item));
                token.ThrowIfCancellationRequested(); files.DeleteItem(item, null); files.PerformOperations();
                files.GetAnyOperationsAborted(out var aborted);
                if (aborted) throw new OperationCanceledException("Recycling was cancelled or unavailable.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                try { if (item is not null) Marshal.ReleaseComObject(item); } catch (Exception ex) { failure ??= ex; }
                try { if (operation is not null) Marshal.ReleaseComObject(operation); } catch (Exception ex) { failure ??= ex; }
            }
            if (failure is OperationCanceledException) completion.TrySetCanceled(token.IsCancellationRequested ? token : new CancellationToken(true));
            else if (failure is not null) completion.TrySetException(new IOException("Could not move the recording to the Recycle Bin. No permanent-delete fallback is used. " + failure.Message, failure));
            else completion.TrySetResult();
        }) { IsBackground = true, Name = "CaptureFlow recycle operation" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint binding, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise([MarshalAs(UnmanagedType.Interface)] object sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] object? sink);
        void RenameItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] object? sink);
        void MoveItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem folder);
        void CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Interface)] object? sink);
        void CopyItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem folder);
        void DeleteItem(IShellItem item, [MarshalAs(UnmanagedType.Interface)] object? sink);
        void DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
        void NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, [MarshalAs(UnmanagedType.Interface)] object? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
