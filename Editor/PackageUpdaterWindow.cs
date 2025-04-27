using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BrandLab360.Packages
{
    [InitializeOnLoad]
    public class PackageUpdaterWindow : EditorWindow
    {
        private static PackageUpdaterWindow window;
        private static int selectedPackageIndex = 0;
        private static int cachePackageIndex = 0;
        private static string selectedPackage = "";
        private static string packagesRootPath = "PATH NOT SET";
        private static string remotePath = "";
        private static string workspacePath = "";

        private static bool m_packageCancelled = false;
        private static Thread m_thread;
        private static Process m_process;
        private static bool m_packageFinished = false;
        private static GitCommand m_GITTask;

        private static string gitPackageAccount = "andySDMF";
        private static Dictionary<string, string> packages = new Dictionary<string, string>();

        private static UpdateMode updateMode = UpdateMode.Update;
        private static string commentText = "";
        private static string commentTitle = "What's new?";
        private static int selectedUpdateMode = 0;

        private static bool previousPushComplete = false;

        private static string m_GITTag = "";
        private static string m_GITBRANCH = "";

        private static string[] ignoredFiles = new string[]
        {
            ".gitattributes",
            ".gitignore"
        };

        private static string[] ignoredDirectories = new string[]
        {
            ".git"
        };

        [MenuItem("BrandLab360/Packages/Package Updater")]
        public static void ShowWindow()
        {
            window = (PackageUpdaterWindow)EditorWindow.GetWindow(typeof(PackageUpdaterWindow));
            window.Show();
        }

        private void OnEnable()
        {
            if (PlayerPrefs.HasKey("PackagesRootPath"))
            {
                packagesRootPath = PlayerPrefs.GetString("PackagesRootPath", packagesRootPath);
            }

            findPackages();
            Versioning.SetDirty();
        }

        void OnGUI()
        {
            if (Application.isPlaying)
            {
                return;
            }

            GUILayout.Label("PACKAGE UPDATER", EditorStyles.boldLabel);
            GUILayout.Space(10);

            GUILayout.Label("Root path for the packages git directories ( e.g. C:/Packages/ )", EditorStyles.miniLabel);
            GUILayout.Space(10);
            GUILayout.Label(packagesRootPath);
            GUILayout.Space(10);

            if (GUILayout.Button("Select Packages Root Directory"))
            {
                var packagesPath = EditorUtility.OpenFolderPanel("Packages Root Directory", "", "");

                if (string.IsNullOrEmpty(packagesPath))
                {
                    return;
                }

                packagesRootPath = packagesPath;

                if (packagesRootPath[packagesRootPath.Length - 1] != '/')
                {
                    packagesRootPath += '/';
                }

                PlayerPrefs.SetString("PackagesRootPath", packagesRootPath);
            }

            var guiEnabled = packagesRootPath == "PATH NOT SET" ? false : true;

            GUI.enabled = guiEnabled;

            GUILayout.Space(10);
            GUILayout.Label("Package:", EditorStyles.miniLabel);
            selectedPackageIndex = EditorGUILayout.Popup(selectedPackageIndex, packages.Keys.ToArray<string>());

            var initialVersion = Versioning.GetInitialVersion();
            var currentVersion = Versioning.GetCurrentVersion(packages.Keys.ToArray<string>()[selectedPackageIndex]);
            var newVersion = Versioning.GetNewVersion(updateMode, packages.Keys.ToArray<string>()[selectedPackageIndex]);

            bool initialCommit = initialVersion.Equals(currentVersion) || string.IsNullOrEmpty(currentVersion) ? true : false;
            string updateText = initialCommit ? "Initial" : "Update";
            currentVersion = initialCommit ? "0.0.0" : currentVersion;

            GUILayout.Label("Current Version: " + currentVersion, EditorStyles.miniLabel);
            GUILayout.Space(10);

            string[] modes = new string[]
            {
                updateText,
                "Patch",
                "Release"
            };

            if (updateText.Equals("Initial"))
            {
                selectedUpdateMode = EditorGUILayout.Popup(selectedUpdateMode, new string[1] { updateText });
            }
            else
            {
                selectedUpdateMode = EditorGUILayout.Popup(selectedUpdateMode, modes);
            }

            if (selectedUpdateMode == 0)
            {
                commentTitle = initialCommit ? "Initial Comment" : "What's new?";
                updateMode = initialCommit ? UpdateMode.Initial : UpdateMode.Update;
                newVersion = initialCommit ? initialVersion : newVersion;

            }
            else if(selectedUpdateMode == 2)
            {
                commentTitle = "Release Notes";
                updateMode = UpdateMode.Release;
            }
            else
            {
                commentTitle = "What's fixed?";
                updateMode = UpdateMode.Patch;
            }

            GUILayout.Space(10);
            GUILayout.Label(commentTitle, EditorStyles.boldLabel);


            commentText = EditorGUILayout.TextArea(commentText, GUILayout.Height(120));

            GUILayout.Label("New Version: " + newVersion, EditorStyles.miniLabel);
            GUILayout.Space(10);

            var buttonEnabled = string.IsNullOrEmpty(commentText) ? false : true;

            GUI.enabled = buttonEnabled;

            if (GUILayout.Button("Update Package"))
            {
                previousPushComplete = false;

                if (packagesRootPath != "PATH NOT SET")
                {
                    if (!Directory.Exists(packagesRootPath))
                    {
                        UnityEngine.Debug.Log("Package root directory doesnt exist. Creating it");
                        Directory.CreateDirectory(packagesRootPath);
                    }
                }
                else
                {
                    UnityEngine.Debug.Log("Cannot update package because root packages path not set!");
                    return;
                }

                selectedPackage = packages.Keys.ToArray<string>()[selectedPackageIndex];
                remotePath = packages.Values.ToArray<string>()[selectedPackageIndex];
                workspacePath = packagesRootPath + packages.Keys.ToArray<string>()[selectedPackageIndex] + "/";

                UnityEngine.Debug.Log("BEGIN UPDATING PACKAGE: " + selectedPackage);

                savePackageMeta(newVersion);

                //check which version of Unity, if old version then ensure we connect to old branch
                UnityEngine.Debug.Log("Git Detecting Unity Version for branch");

#if UNITY_2023_1_OR_NEWER
                m_GITBRANCH = "main";
                m_GITTag = packages.Keys.ToArray<string>()[selectedPackageIndex] + "/" + m_GITBRANCH + "/Release/" + newVersion;
                ExecuteCommand("/k git checkout main" , workspacePath, GitCommand.BRANCH);
#else
                m_GITBRANCH = "legacy";
                m_GITTag = packages.Keys.ToArray<string>()[selectedPackageIndex] + "/" + m_GITBRANCH + "/Release/" + newVersion;
                ExecuteCommand("/k git checkout legacy", workspacePath, GitCommand.BRANCH);
#endif
            }

            GUI.enabled = true;
        }

        public static void ExecuteCommand(string args, string path, GitCommand gitCommand)
        {
            m_thread = new Thread(delegate () { Command(args, path, gitCommand); });
            m_thread.Start();
        }

        static void Command(string args, string path, GitCommand gitCommand)
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = args;
            process.StartInfo.WorkingDirectory = path;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ErrorDialog = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            m_GITTask = gitCommand;
            bool createLocalLegacyBranch = false;

            process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if(e.Data.Equals("error: pathspec 'legacy' did not match any file(s) known to git"))
                    {
                        previousPushComplete = true;
                        createLocalLegacyBranch = true;
                    }

                    UnityEngine.Debug.Log(e.Data);
                }
            });

            process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    UnityEngine.Debug.Log(e.Data);
                }
            });

            m_process = process;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            process.Close();

            UnityEngine.Debug.Log("Finished command: " + gitCommand.ToString());

            if(previousPushComplete)
            {

            }

            if(gitCommand == GitCommand.BRANCH)
            {
                if(createLocalLegacyBranch)
                {
                    UnityEngine.Debug.Log("Git creating legacy branch");
                    ExecuteCommand("/k git checkout -b legacy", workspacePath, GitCommand.BRANCH);
                }
                else
                {
                    if (Directory.Exists(workspacePath))
                    {
                        GetPackageProgressUploadValue();
                        UnityEngine.Debug.Log("Git Workspace exists, pulling first");
                        ExecuteCommand("/k git pull " + remotePath, workspacePath, GitCommand.PULL);
                    }
                    else
                    {
                        GetPackageProgressUploadValue();
                        UnityEngine.Debug.Log("Git Workspace doesnt exist, cloning first");
                        ExecuteCommand("/k git clone " + remotePath, packagesRootPath, GitCommand.CLONE);
                    }
                }
            }
            else if (gitCommand == GitCommand.CLONE || gitCommand == GitCommand.PULL)
            {
                ReplacePackageFiles();
            }
            else if (gitCommand == GitCommand.ADD)
            {
                UnityEngine.Debug.Log("Git Commit");
                ExecuteCommand("/k git commit -m \" Package Update \"", workspacePath, GitCommand.COMMIT);
            }
            else if (gitCommand == GitCommand.COMMIT)
            {
                UnityEngine.Debug.Log("Git Push");
                UnityEngine.Debug.Log(remotePath);
                UnityEngine.Debug.Log(workspacePath);
                
                if(updateMode == UpdateMode.Release)
                {
                    ExecuteCommand("/k git push " + remotePath, workspacePath, GitCommand.CREATETAG);
                }
                else
                {
                    ExecuteCommand("/k git push " + remotePath, workspacePath, GitCommand.PUSH);
                }
            }
            else if (gitCommand == GitCommand.CREATETAG)
            {
                UnityEngine.Debug.Log("Git Tag");
                ExecuteCommand("/k git tag -a " + m_GITTag + " -m  \" Releasing Version: \"" + m_GITTag, workspacePath, GitCommand.PUSHTAG);
            }
            else if (gitCommand == GitCommand.PUSHTAG)
            {
                previousPushComplete = true;
                UnityEngine.Debug.Log("Git Push Tag");
                ExecuteCommand("/k git push origin " + m_GITTag, workspacePath, GitCommand.PUSH);
            }
            else if (gitCommand == GitCommand.PUSH)
            {
                UnityEngine.Debug.Log("FINISHED UPDATING PACKAGE: " + selectedPackage);
                m_packageFinished = true;
                commentText = "";
                Versioning.SetDirty();
            }
        }

        private static async void GetPackageProgressUploadValue()
        {
            EditorUtility.ClearProgressBar();

            float progressValue = 0.0f;
            m_packageCancelled = false;
            m_packageFinished = false;

            while (!m_packageCancelled && progressValue < 1.0f)
            {
                if (!m_packageFinished)
                {
                    if (progressValue < 0.7f)
                    {
                        progressValue += 0.0001f;
                    }
                }
                else
                {
                    progressValue += 0.01f;
                }

                await Task.Delay(1);

                if (EditorUtility.DisplayCancelableProgressBar("Packaging", m_GITTask.ToString() + ":: Package: " + selectedPackage, progressValue))
                {
                    m_packageCancelled = true;
                    UnityEngine.Debug.Log("package aborted");

                    if (m_process != null) m_process.Kill();
                    if (m_thread != null) m_thread.Abort();
                }
            }

            EditorUtility.ClearProgressBar();
        }

        public static void ReplacePackageFiles()
        {
            var path = Directory.GetCurrentDirectory() + "\\Assets\\" + packages.Keys.ToArray<string>()[selectedPackageIndex];

            UnityEngine.Debug.Log("Deleting files from workspace");

            DeleteSubDirectories(workspacePath);

            UnityEngine.Debug.Log("Copying files from plugin to workspace");

            CopyFilesRecursively(path, workspacePath);
        }

        public static void DeleteSubDirectories(string targetDir, bool topLevel = true)
        {
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return;

            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                bool found = false;

                foreach (string ignoredFile in ignoredFiles)
                {
                    if (file.Contains(ignoredFile)) { found = true; }
                }

                if (!found)
                {
                    File.Delete(file);
                }
            }

            foreach (string dir in dirs)
            {
                bool found = false;

                foreach (string ignoredDir in ignoredDirectories)
                {
                    if (dir.Contains(ignoredDir)) { found = true; }
                }

                if (!found)
                {
                    DeleteSubDirectories(dir, false);
                }
            }

            if (!topLevel)
            {
                Directory.Delete(targetDir, false);
            }
        }

        private static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }

            foreach (string dirPath in Directory.GetDirectories(targetPath))
            {
                if (dirPath.Contains("Samples"))
                {
                    UnityEngine.Debug.Log("Fixing Samples Folder");

                    var newPath = dirPath + "~";
                    Directory.Move(dirPath, newPath);
                    var metaFile = dirPath + ".meta";

                    if (File.Exists(metaFile))
                    {
                        File.Delete(metaFile);
                    }
                }
            }

            UnityEngine.Debug.Log("Git Add");
            ExecuteCommand("/k git add --all", workspacePath, GitCommand.ADD);

        }

        private static void findPackages()
        {
            var path = Directory.GetCurrentDirectory() + "\\Assets\\";
            string[] dirs = Directory.GetDirectories(path);
            List<string> foundPackages = new List<string>();

            foreach (string dir in dirs)
            {
                string[] files = Directory.GetFiles(dir);

                foreach (string file in files)
                {
                    var fileArr = file.Split('\\');

                    if (fileArr.Length > 0)
                    {
                        if (fileArr[fileArr.Length - 1] == "package.json")
                        {
                            var dirArr = dir.Split('\\');

                            if (dirArr.Length > 0)
                            {
                                foundPackages.Add(dirArr[dirArr.Length - 1]);
                            }
                        }
                    }
                }
            }

            foreach (var packageName in foundPackages)
            {
                if (!packages.ContainsKey(packageName))
                {
                    var remotePackagePath = "https://github.com/" + gitPackageAccount + "/" + packageName + ".git";
                    packages.Add(packageName, remotePackagePath);
                }
            }
        }

        public enum GitCommand { BRANCH, CLONE, PULL, PUSH, COMMIT, ADD, CREATETAG, PUSHTAG, CREATERELEASE};

        private static void savePackageMeta(string newVersion)
        {
            var packagePath = Path.Combine("Assets", packages.Keys.ToArray<string>()[selectedPackageIndex]);
            var path = Path.Combine(Directory.GetCurrentDirectory(), packagePath);
            var jsonFile = "package.json";
            var logFile = "CHANGELOG.md";
            var jsonPath = Path.Combine(path, jsonFile);
            var logPath = Path.Combine(path, logFile);

            File.WriteAllLines(jsonPath, Versioning.GetNewPackageVersion());
            AssetDatabase.ImportAsset(Path.Combine(packagePath, jsonFile));

            var log = File.ReadAllLines(logPath);
            var newLog = new string[log.Length + 1];
            newLog[0] = "    Release: " + newVersion + '\n' + '\n' + commentText + '\n';

            for (int i = 0; i < log.Length; i++)
            {
                var prepend = "";
                if (log[i].Contains("Release:")) { prepend = "    "; }
                newLog[i + 1] = prepend + log[i];
            }

            File.WriteAllLines(logPath, newLog);
            AssetDatabase.ImportAsset(Path.Combine(packagePath, logFile));
        }

        private void OnInspectorUpdate()
        {
            if(selectedPackageIndex != cachePackageIndex)
            {
                cachePackageIndex = selectedPackageIndex;
                Versioning.SetDirty();
            }

        }
    }
}