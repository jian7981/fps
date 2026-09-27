using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using System.IO.Compression;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine.Networking;
#endif

/// <summary>
/// [Haon SD Series Essential Package Installer]
/// Checks for required packages (ToonShader, SpringBone) when the attached scene is executed
/// and prompts the user with a popup dialog for automatic installation if missing.
/// </summary>
public class HaonSDEssentialPackageInstaller : MonoBehaviour
{
    // SpringBone package download URL (HTTP ZIP download fallback for environments without Git)
    private const string SPRINGBONE_ZIP_URL = "https://github.com/unity3d-jp/UnityChanSpringBone/archive/refs/tags/1.2.0-preview.zip";

#if UNITY_EDITOR
    private static AddRequest s_AddRequest;
    private static string[] s_MissingPackages;
    private static int s_CurrentIndex = 0;
    private static UnityWebRequest s_DownloadRequest;
#endif

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitOnEditorLoad()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CheckAndInstall();
            }
        };
    }
#endif

    private void Awake()
    {
        // Check for missing packages when scene is executed
        CheckAndInstall();
    }

    /// <summary>
    /// Context menu command to trigger package verification manually from the Inspector
    /// </summary>
    [ContextMenu("Check Packages Now")]
    public void ForceCheckAndInstall()
    {
#if UNITY_EDITOR
        CheckAndInstall();
#endif
    }

    /// <summary>
    /// Checks for required package installations and displays a confirmation dialog if missing.
    /// </summary>
    public static void CheckAndInstall()
    {
#if UNITY_EDITOR
        // 1. Verify existence of Packages/manifest.json
        string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
        if (!File.Exists(manifestPath)) return;

        string manifestText = File.ReadAllText(manifestPath);

        // 2. Check for ToonShader package existence
        bool hasToonShader = manifestText.Contains("com.unity.toonshader");

        // 3. Check for SpringBone package existence (local folder or manifest.json)
        string localSpringbonePkgJson = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.unity.springbone", "package.json");
        bool hasLocalSpringboneFile = File.Exists(localSpringbonePkgJson);

        // Clean up manifest entry if local path is listed but target directory does not exist
        if (manifestText.Contains("file:com.unity.springbone") && !hasLocalSpringboneFile)
        {
            Debug.LogWarning("[HaonSDEssentialPackageInstaller] Local com.unity.springbone package folder is missing. Cleaning up manifest.json entry.");
            RemoveManifestDependency(manifestPath, "com.unity.springbone");
            manifestText = File.ReadAllText(manifestPath);
        }

        bool hasSpringBone = hasLocalSpringboneFile ||
                             (manifestText.Contains("com.unity.springbone") && !manifestText.Contains("file:com.unity.springbone"));

        // 4. Exit if all required packages are properly installed
        if (hasToonShader && hasSpringBone)
        {
            Debug.Log("✅ [HaonSDEssentialPackageInstaller] All required packages (ToonShader, SpringBone) are already installed.");
            return;
        }

        // 5. Generate missing package list
        System.Collections.Generic.List<string> missingList = new System.Collections.Generic.List<string>();
        if (!hasToonShader) missingList.Add("com.unity.toonshader (Unity Toon Shader)");
        if (!hasSpringBone) missingList.Add("com.unity.springbone (Unity-chan SpringBone)");

        s_MissingPackages = missingList.ToArray();

        // 6. Display user confirmation popup dialog
        string packageNamesStr = string.Join("\n• ", s_MissingPackages);
        bool confirmInstall = EditorUtility.DisplayDialog(
            "[Haon SD Series] Required Package Notification",
            $"[Haon SD Series]\nThe following required packages are not installed in your project:\n\n• {packageNamesStr}\n\nWould you like to install these packages automatically now?",
            "Yes (Install Now)",
            "No (Cancel)"
        );

        if (confirmInstall)
        {
            s_CurrentIndex = 0;
            StartNextInstallation();
        }
        else
        {
            Debug.Log("[HaonSDEssentialPackageInstaller] User cancelled automatic package installation.");
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Installs missing packages sequentially from the missing package list.
    /// </summary>
    private static void StartNextInstallation()
    {
        if (s_MissingPackages == null || s_CurrentIndex >= s_MissingPackages.Length)
        {
            Debug.Log("✅ [HaonSDEssentialPackageInstaller] All requested packages have been installed successfully.");
            EditorUtility.ClearProgressBar();

            EditorApplication.delayCall += () =>
            {
                Client.Resolve();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (EditorApplication.isPlaying)
                {
                    Debug.Log("🔄 [HaonSDEssentialPackageInstaller] Stopping Play Mode to force Unity Domain Reload and rebind SpringBone component script references. Please press Play again!");
                    EditorApplication.isPlaying = false;
                }
            };
            return;
        }

        string rawTarget = s_MissingPackages[s_CurrentIndex];
        string pkgIdentifier = rawTarget.Split(' ')[0];

        // Handle SpringBone package installation (local backup or HTTP ZIP download)
        if (pkgIdentifier.Contains("springbone"))
        {
            string backupPath = FindBackupPackageFolder("SpringBonePackage");
            string targetPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.unity.springbone");

            // Copy from local embedded backup package if available in Assets folder
            if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
            {
                try
                {
                    CopyDirectory(backupPath, targetPath);
                    SetManifestDependency("com.unity.springbone", "file:com.unity.springbone");
                    Debug.Log("✅ [HaonSDEssentialPackageInstaller] Successfully installed com.unity.springbone from local backup package.");
                    s_CurrentIndex++;
                    StartNextInstallation();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HaonSDEssentialPackageInstaller] Local package copy failed ({ex.Message}). Falling back to HTTP ZIP download.");
                }
            }

            // Download via HTTP ZIP
            StartHttpZipDownload(SPRINGBONE_ZIP_URL, targetPath);
            return;
        }

        // Standard UPM package installation (e.g., ToonShader)
        Debug.Log($"[HaonSDEssentialPackageInstaller] Starting package installation ({s_CurrentIndex + 1}/{s_MissingPackages.Length}): {pkgIdentifier}");
        s_AddRequest = Client.Add(pkgIdentifier);
        EditorApplication.update += MonitorRequestProgress;
    }

    /// <summary>
    /// Downloads the SpringBone package via HTTP ZIP.
    /// </summary>
    private static void StartHttpZipDownload(string zipUrl, string targetPackagePath)
    {
        Debug.Log($"[HaonSDEssentialPackageInstaller] Starting HTTP ZIP download: {zipUrl}");
        EditorUtility.DisplayProgressBar("[Haon SD Series]", "Downloading SpringBone Package...", 0.1f);

        s_DownloadRequest = UnityWebRequest.Get(zipUrl);
        s_DownloadRequest.SendWebRequest();
        EditorApplication.update += MonitorDownloadProgress;
    }

    /// <summary>
    /// Monitors progress for HTTP ZIP download.
    /// </summary>
    private static void MonitorDownloadProgress()
    {
        if (s_DownloadRequest == null)
        {
            EditorApplication.update -= MonitorDownloadProgress;
            return;
        }

        EditorUtility.DisplayProgressBar("[Haon SD Series]", $"Downloading SpringBone Package... ({(int)(s_DownloadRequest.downloadProgress * 100)}%)", s_DownloadRequest.downloadProgress);

        if (s_DownloadRequest.isDone)
        {
            EditorApplication.update -= MonitorDownloadProgress;
            EditorUtility.ClearProgressBar();

            if (s_DownloadRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    byte[] zipData = s_DownloadRequest.downloadHandler.data;
                    string tempDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SpringBoneZip");
                    string zipFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "springbone.zip");

                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);

                    File.WriteAllBytes(zipFilePath, zipData);
                    ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                    // Locate directory containing package.json inside extracted folder
                    string packageJsonDir = FindPackageJsonDirectory(tempDir);
                    if (!string.IsNullOrEmpty(packageJsonDir))
                    {
                        string targetPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "com.unity.springbone");
                        if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
                        CopyDirectory(packageJsonDir, targetPath);

                        SetManifestDependency("com.unity.springbone", "file:com.unity.springbone");
                        Debug.Log("✅ [HaonSDEssentialPackageInstaller] HTTP ZIP download and com.unity.springbone installation completed.");
                    }
                    else
                    {
                        Debug.LogError("[HaonSDEssentialPackageInstaller] Could not find package.json inside downloaded ZIP archive.");
                    }

                    // Clean up temporary files
                    if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HaonSDEssentialPackageInstaller] Error extracting SpringBone ZIP: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError($"[HaonSDEssentialPackageInstaller] HTTP ZIP download failed: {s_DownloadRequest.error}");
            }

            s_DownloadRequest.Dispose();
            s_DownloadRequest = null;

            s_CurrentIndex++;
            StartNextInstallation();
        }
    }

    /// <summary>
    /// Monitors UPM Client.Add progress.
    /// </summary>
    private static void MonitorRequestProgress()
    {
        if (s_AddRequest != null && s_AddRequest.IsCompleted)
        {
            EditorApplication.update -= MonitorRequestProgress;

            if (s_AddRequest.Status == StatusCode.Success)
            {
                Debug.Log($"✅ [HaonSDEssentialPackageInstaller] Package installation completed: {s_AddRequest.Result?.packageId ?? s_MissingPackages[s_CurrentIndex]}");
            }
            else if (s_AddRequest.Status >= StatusCode.Failure)
            {
                Debug.LogWarning($"⚠️ [HaonSDEssentialPackageInstaller] UPM Client.Add failed ({s_MissingPackages[s_CurrentIndex]}): {s_AddRequest.Error?.message}. Attempting direct manifest.json registration.");
                TryDirectManifestFallback(s_MissingPackages[s_CurrentIndex]);
            }

            s_CurrentIndex++;
            StartNextInstallation();
        }
    }

    /// <summary>
    /// Searches for a local backup package folder by name in the Assets directory.
    /// </summary>
    private static string FindBackupPackageFolder(string folderName)
    {
        try
        {
            string assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            if (!Directory.Exists(assetsPath)) return null;

            string[] directories = Directory.GetDirectories(assetsPath, folderName, SearchOption.AllDirectories);
            foreach (var dir in directories)
            {
                if (File.Exists(Path.Combine(dir, "package.json")))
                {
                    return dir;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HaonSDEssentialPackageInstaller] Exception while searching for backup package: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Finds the directory containing package.json inside search root.
    /// </summary>
    private static string FindPackageJsonDirectory(string searchRoot)
    {
        string[] files = Directory.GetFiles(searchRoot, "package.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string content = File.ReadAllText(file);
            if (content.Contains("com.unity.springbone"))
            {
                return Path.GetDirectoryName(file);
            }
        }
        return files.Length > 0 ? Path.GetDirectoryName(files[0]) : null;
    }

    /// <summary>
    /// Adds package dependency entry into Packages/manifest.json.
    /// </summary>
    private static void SetManifestDependency(string pkgName, string pkgVal)
    {
        try
        {
            string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;

            string json = File.ReadAllText(manifestPath);
            json = RemoveDependencyFromJson(json, pkgName);

            int depIdx = json.IndexOf("\"dependencies\": {");
            if (depIdx >= 0)
            {
                int insertPos = depIdx + "\"dependencies\": {".Length;
                string newLine = $"\n    \"{pkgName}\": \"{pkgVal}\",";
                json = json.Insert(insertPos, newLine);
                File.WriteAllText(manifestPath, json);
                AssetDatabase.Refresh();
                Debug.Log($"✅ [HaonSDEssentialPackageInstaller] Registered {pkgName} (\"{pkgVal}\") in manifest.json.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HaonSDEssentialPackageInstaller] Exception while setting manifest dependency: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes specified package entry from Packages/manifest.json.
    /// </summary>
    private static void RemoveManifestDependency(string manifestPath, string pkgName)
    {
        try
        {
            if (!File.Exists(manifestPath)) return;
            string json = File.ReadAllText(manifestPath);
            json = RemoveDependencyFromJson(json, pkgName);
            File.WriteAllText(manifestPath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HaonSDEssentialPackageInstaller] Exception while removing manifest dependency: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes lines containing target package name from JSON string.
    /// </summary>
    private static string RemoveDependencyFromJson(string json, string pkgName)
    {
        string[] lines = json.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        System.Collections.Generic.List<string> resultLines = new System.Collections.Generic.List<string>();
        foreach (var line in lines)
        {
            if (!line.Contains($"\"{pkgName}\""))
            {
                resultLines.Add(line);
            }
        }
        return string.Join("\n", resultLines);
    }

    /// <summary>
    /// Fallback registration in manifest.json when UPM Client.Add fails.
    /// </summary>
    private static void TryDirectManifestFallback(string pkgIdentifier)
    {
        if (pkgIdentifier.Contains("toonshader"))
        {
            SetManifestDependency("com.unity.toonshader", "0.15.0-preview");
        }
        else if (pkgIdentifier.Contains("springbone"))
        {
            SetManifestDependency("com.unity.springbone", "file:com.unity.springbone");
        }
    }

    /// <summary>
    /// Copies directory and all contained files recursively to target destination.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = file.Substring(sourceDir.Length + 1);
            string targetFile = Path.Combine(destinationDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
            File.Copy(file, targetFile, true);
        }
    }
#endif
}