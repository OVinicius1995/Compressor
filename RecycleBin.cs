using System.Runtime.InteropServices;

[ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecycleProgress
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, nint item, nint name);
    [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint created);
    [PreserveSig] int PreMoveItem(uint flags, nint item, nint folder, nint name);
    [PreserveSig] int PostMoveItem(uint flags, nint item, nint folder, nint name, int result, nint created);
    [PreserveSig] int PreCopyItem(uint flags, nint item, nint folder, nint name);
    [PreserveSig] int PostCopyItem(uint flags, nint item, nint folder, nint name, int result, nint created);
    [PreserveSig] int PreDeleteItem(uint flags, nint item);
    [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
    [PreserveSig] int PreNewItem(uint flags, nint folder, nint name);
    [PreserveSig] int PostNewItem(uint flags, nint folder, nint name, nint template, uint attributes, int result, nint created);
    [PreserveSig] int UpdateProgress(uint total, uint progress);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleProgress : IRecycleProgress
{
    // O Shell pode propor exclusão definitiva quando o volume ou o arquivo não admite reciclagem.
    public int StartOperations() => 0;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint flags, nint item, nint name) => 0;
    public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
    public int PreMoveItem(uint flags, nint item, nint folder, nint name) => 0;
    public int PostMoveItem(uint flags, nint item, nint folder, nint name, int result, nint created) => 0;
    public int PreCopyItem(uint flags, nint item, nint folder, nint name) => 0;
    public int PostCopyItem(uint flags, nint item, nint folder, nint name, int result, nint created) => 0;
    public int PreDeleteItem(uint flags, nint item) => (flags & 0x80) != 0 ? 0 : unchecked((int)0x80004004);
    public int PostDeleteItem(uint flags, nint item, int result, nint created) => result;
    public int PreNewItem(uint flags, nint folder, nint name) => 0;
    public int PostNewItem(uint flags, nint folder, nint name, nint template, uint attributes, int result, nint created) => 0;
    public int UpdateProgress(uint total, uint progress) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}

// IFileOperation permite exigir a Lixeira no Windows 10+ (FOFX_RECYCLEONDELETE).
internal static class RecycleBin
{
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint window);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink);
        void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint destination, nint name, nint sink);
        void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, nint name, nint sink);
        void CopyItems(nint items, nint destination);
        void DeleteItem(nint item, [MarshalAs(UnmanagedType.Interface)] IRecycleProgress sink);
        void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, out nint item);

    internal static void Move(string path)
    {
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
        nint item = 0;
        try
        {
            operation.SetOperationFlags(0x80000 | 0x400 | 0x10 | 0x4 | 0x100000); // recycle, no UI, early failure
            SHCreateItemFromParsingName(path, 0, new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), out item);
            operation.DeleteItem(item, new RecycleProgress());
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (aborted || File.Exists(path)) throw new IOException($"Não foi possível enviar {Path.GetFileName(path)} para a Lixeira.");
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }
}
