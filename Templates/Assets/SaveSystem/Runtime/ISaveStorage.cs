// Абстракция файлового доступа для системы сохранений. Сегодня есть только FileSaveStorage
// (Windows/macOS/Linux/Steam, обычный System.IO). Порт на консоль потребует своего SDK для
// сохранений (доступен только под NDA) — тогда под него пишется отдельный ISaveStorage, а
// AtomicFileIO/GuidListSaveUtility/SaveManager не меняются.
public interface ISaveStorage
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllTextAtomic(string path, string contents);
    void EnsureDirectory(string path);
    void CopyDirectory(string sourceDir, string destDir);
    void SwapDirectory(string tempDir, string targetDir);
    void RecoverIfNeeded(string targetDir);
    void DeleteDirectory(string path);
}
