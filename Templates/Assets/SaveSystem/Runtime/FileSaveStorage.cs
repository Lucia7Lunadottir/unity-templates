using System.IO;

public class FileSaveStorage : ISaveStorage
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllTextAtomic(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, null);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    public void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public void CopyDirectory(string sourceDir, string destDir)
    {
        EnsureDirectory(destDir);
        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(filePath));
            File.Copy(filePath, destFile, overwrite: true);
        }
        foreach (string dirPath in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dirPath));
            CopyDirectory(dirPath, destSubDir);
        }
    }

    // Атомарная замена содержимого targetDir содержимым tempDir. Устойчиво к краху между
    // шагами — RecoverIfNeeded довершает/откатывает то, что не успело завершиться.
    public void SwapDirectory(string tempDir, string targetDir)
    {
        string oldDir = targetDir + ".old";
        if (Directory.Exists(oldDir))
        {
            Directory.Delete(oldDir, recursive: true);
        }
        if (Directory.Exists(targetDir))
        {
            Directory.Move(targetDir, oldDir);
        }
        Directory.Move(tempDir, targetDir);
        if (Directory.Exists(oldDir))
        {
            Directory.Delete(oldDir, recursive: true);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void RecoverIfNeeded(string targetDir)
    {
        string oldDir = targetDir + ".old";
        string tmpDir = targetDir + ".tmp";

        // targetDir пропал, а .old остался — значит крах случился между переименованием
        // старой папки и переносом новой. Откатываемся к последнему целому состоянию.
        if (!Directory.Exists(targetDir) && Directory.Exists(oldDir))
        {
            Directory.Move(oldDir, targetDir);
        }

        if (Directory.Exists(targetDir))
        {
            if (Directory.Exists(oldDir))
            {
                Directory.Delete(oldDir, recursive: true);
            }
            if (Directory.Exists(tmpDir))
            {
                Directory.Delete(tmpDir, recursive: true);
            }
        }
    }
}
