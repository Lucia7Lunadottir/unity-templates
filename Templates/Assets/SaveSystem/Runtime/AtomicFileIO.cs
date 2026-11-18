// Тонкий фасад над SaveManager.Storage — весь остальной код сохранений (checkpoint,
// GuidListSaveUtility и т.д.) зовёт эти статические методы и не знает про ISaveStorage напрямую.
public static class AtomicFileIO
{
    public static void WriteAllTextAtomic(string path, string contents) => SaveManager.Storage.WriteAllTextAtomic(path, contents);
    public static void CopyDirectory(string sourceDir, string destDir) => SaveManager.Storage.CopyDirectory(sourceDir, destDir);
    public static void SwapDirectory(string tempDir, string targetDir) => SaveManager.Storage.SwapDirectory(tempDir, targetDir);
    public static void RecoverIfNeeded(string targetDir) => SaveManager.Storage.RecoverIfNeeded(targetDir);
    public static void DeleteDirectory(string path) => SaveManager.Storage.DeleteDirectory(path);
}
