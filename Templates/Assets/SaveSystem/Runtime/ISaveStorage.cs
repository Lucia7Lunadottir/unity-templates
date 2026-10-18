namespace PG.SaveSystem
{
    // File access abstraction; implement a custom one for platforms with their own save SDK
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
}
