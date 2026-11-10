using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Shanmukha.AutoSave.Editor
{
    [InitializeOnLoad]
    public class AutoSaveTool : EditorWindow // Inheriting from editor window let's us draw a custom window!
    {
        // Stores exact future time when the next trigger should happen
        // Start at -1 so OnEditorUpdate knows it needs to initialize the timer safely
        private static double _nextSaveTime = -1;
        private Vector2 _scrollPos;

        // Keys for saving our settings in Unity Editor's memory
        private const string ENABLED_KEY = "SmartAutoSave_Enabled";
        private const string PLAY_SAVE_KEY = "SmartAutoSave_SaveOnPlay";
        private const string PREFAB_SAVE_KEY = "SmartAutoSave_SavePrefabs";
        private const string LOGS_ENABLED_KEY = "SmartAutoSave_ShowLogs";
        private const string TOAST_ENABLED_KEY = 
        "SmartAutoSave_ShowToast";
        private const string INTERVAL_KEY = "SmartAutoSave_IntervalSeconds";
        private const string BACKUP_ENABLED_KEY = "SmartAutoSave_BackupEnabled";
        private const string MAX_BACKUPS_KEY = "SmartAutoSave_MaxBackups";

        // Properties that automatically load / save to EditorPrefs
        // made public so our Scen view overlay can read / toggle it!
        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(ENABLED_KEY, true); // Default: true
            set => EditorPrefs.SetBool(ENABLED_KEY, value);
        }

        public static bool SaveOnPlay
        {
            get => EditorPrefs.GetBool(PLAY_SAVE_KEY, true); // Default: true
            set => EditorPrefs.SetBool(PLAY_SAVE_KEY, value);
        }

        public static bool SavePrefabs
        {
            get => EditorPrefs.GetBool(PREFAB_SAVE_KEY, true); // Default = true;
            set => EditorPrefs.SetBool(PREFAB_SAVE_KEY, value);
        }

        public static float SaveInterval
        {
            get => EditorPrefs.GetFloat(INTERVAL_KEY, 60f); // Default: 60 seconds
            set => EditorPrefs.SetFloat(INTERVAL_KEY, Mathf.Max(5f, value)); // Minimum of 5 Seconds interval
        }

        public static bool ShowSceneToast
        {
            get => EditorPrefs.GetBool(TOAST_ENABLED_KEY, true); // Default = true
            set => EditorPrefs.SetBool(TOAST_ENABLED_KEY, value);
        }

        public static bool EnableBackups
        {
            get => EditorPrefs.GetBool(BACKUP_ENABLED_KEY, true); // Default: true
            set => EditorPrefs.SetBool(BACKUP_ENABLED_KEY, value);
        }

        public static int MaxBackups
        {
            get => EditorPrefs.GetInt(MAX_BACKUPS_KEY, 5);
            set => EditorPrefs.SetInt(MAX_BACKUPS_KEY, Mathf.Clamp(value, 1, 20));
        }

        public static bool ShowConsoleLogs
        {
            get => EditorPrefs.GetBool(LOGS_ENABLED_KEY, true); // Default: true
            set => EditorPrefs.SetBool(LOGS_ENABLED_KEY, value);
        } 

        // Helper property so Scene View  overlay can read the remaining seconds
        public static double TimeRemaining => System.Math.Max(0, _nextSaveTime - EditorApplication.timeSinceStartup);

        // Stores backups outside Assets/ (in ProjectRoot/AutoSaveBackups) so Unity doesn't lag re-importing them!
        private static string BackupFolderPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../AutoSaveBackups"));
        
        static AutoSaveTool()
        {
            // Hook our OnEditorUpdate method into Unity's background loop
            EditorApplication.update += OnEditorUpdate;

            // Hook into the play button state change
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // [MenuItem] creates a clickable button in Unity's top menu bar!
        [MenuItem("Tools/Smart AutoSave Settings")]
        public static void ShowWindow()
        {
            // Open (or focuses) our custom window
            AutoSaveTool window = GetWindow<AutoSaveTool>("AutoSave Settings");
            window.minSize = new Vector2(300, 160);
        }

        // OnGUI draws the buttons and slider inside our custom editor
        private void OnGUI()
        {
            // Fix label truncation so longer text lables fit cleanly!
            EditorGUIUtility.labelWidth = 180f;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Smart AutoSave Configuration", EditorStyles.boldLabel);
            GUILayout.Space(5);

            // Toggle for enabling or disabling the timer
            IsEnabled = EditorGUILayout.Toggle("Enable AutoSave", IsEnabled);

            // Toggle for saving right when we press play button
            SaveOnPlay = EditorGUILayout.Toggle("Enable Save before Play mode", SaveOnPlay);

            // Toggle for saving in prefab mode
            SavePrefabs = EditorGUILayout.Toggle("Save in Prefab Mode", SavePrefabs);


            // Number box for the interval in seconds
            EditorGUI.BeginChangeCheck();
            float newInterval = EditorGUILayout.FloatField("Interval (seconds)", SaveInterval);
            if(EditorGUI.EndChangeCheck())
            {
                SaveInterval = newInterval;
                ResetTimer(); // Restart the timer whenever the user changes the interval
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Notifications & Feedback", EditorStyles.boldLabel);

            // Toggle for enabling or disabling the console logs
            ShowConsoleLogs = EditorGUILayout.Toggle("Enable Console Logs", ShowConsoleLogs);

            // Toggle for enabling or disabling the Toast
            ShowSceneToast = EditorGUILayout.Toggle("Show Scene View Toast", ShowSceneToast);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Rolling Backup Settings", EditorStyles.boldLabel);
            EnableBackups = EditorGUILayout.Toggle("Create Scene Backups", EnableBackups);

            if(EnableBackups)
            {
                MaxBackups = EditorGUILayout.IntSlider("Max backups to keep", MaxBackups, 1, 10);
            }

            GUILayout.Space(10);

            // Live Countdown display
            if(IsEnabled && !EditorApplication.isPlaying)
            {
                double timeLeft = System.Math.Max(0, _nextSaveTime - EditorApplication.timeSinceStartup);
                EditorGUILayout.HelpBox($"Next check in: {timeLeft:F1} seconds", MessageType.Info);
                Repaint(); // keeps the countdown ticking smoothly in window
            }
            else
            {
                EditorGUILayout.HelpBox("AutoSave is currently paused!", MessageType.Warning);
            }

            // Manual trigger and backups folder
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            if(GUILayout.Button("Force Save & Backup Now"))
            {
                TryAutoSave("Manual Save", forceSave: true);
                ResetTimer();
            }

            if(GUILayout.Button("Open Backup Folder"))
            {
                EnsureBackupFolderAndGitIgnore();
                EditorUtility.RevealInFinder(BackupFolderPath);
            }

            EditorGUILayout.EndHorizontal();

            // Display Exisiting backups
            DrawBackupList();
        }

        private void DrawBackupList()
        {
            if(!Directory.Exists(BackupFolderPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(BackupFolderPath, "*.unity").OrderByDescending(f=>File.GetCreationTime(f)).ToArray();

            if(files.Length == 0)
            {
                return;
            }

            GUILayout.Space(12);

            // header row with "Clear All" button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Recent Backups ({files.Length}): ", EditorStyles.boldLabel);
            if(GUILayout.Button("Clear All", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                if(EditorUtility.DisplayDialog("Clear All backups?", "Are you sure you want to delete all backup scene files?", "Delete All", "Cancel"))
                {
                    foreach(string file in files)
                    {
                        File.Delete(file);
                    }
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(140));

            foreach(string file in files)
            {
                FileInfo info = new FileInfo(file);
                float sizeKB = info.Length / 1024f;

                EditorGUILayout.BeginHorizontal("box");

                // File name + Size in KB
                GUILayout.Label($"{Path.GetFileNameWithoutExtension(file)} ({sizeKB:F1} KB)", EditorStyles.miniLabel);

                if(GUILayout.Button("Restore", EditorStyles.miniButton, GUILayout.Width(65)))
                {
                    RestoreBackup(file);
                }

                // individual Delete Button ("X)
                if(GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    File.Delete(file);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private static void OnEditorUpdate()
        {
            // Safe initialization on the first update frame after Unity finishes loading!
            if(_nextSaveTime < 0)
            {
                ResetTimer();
            }

            if(!IsEnabled)
            {
                return;
            }

            // SAFETY GUARD: Never auto save when we are in play mode or compiling scripts
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            // NEW SAFETY GUARD: If the user is actively holding down the mouse button
            // (e.g. dragging an object or slider), wait until they release it
            if(GUIUtility.hotControl != 0)
            {
                return;
            }

            // Check if the editor's stopwatch has reached our target time.
            if(EditorApplication.timeSinceStartup >= _nextSaveTime)
            {
                TryAutoSave("Timer");
                ResetTimer();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // ExitingEditMode happens the exact instant you clik play, before play mode starts!
            if(IsEnabled && SaveOnPlay && state == PlayModeStateChange.ExitingEditMode)
            {
                TryAutoSave("Pre-Play Mode");
            }
            else if(state == PlayModeStateChange.EnteredEditMode)
            {
                // Reset the timer fresh when returning from play mode!
                ResetTimer();
            }
        }

        private static void TryAutoSave(string reason, bool forceSave = false)
        {
            List<string> savedItems = new List<string>();

            // 1. Check if the user is currently inside Isolated Prefab mode!
            if(SavePrefabs)
            {
                PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if(prefabStage != null && (prefabStage.scene.isDirty || forceSave))
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabStage.assetPath);
                    savedItems.Add($"Prefab '{prefabStage.prefabContentsRoot.name}'");
                }
            }

            // 2. Multi-Scene Support: Loop through all currently open scenes in the Heirarchy
            List<Scene> dirtyScenes = new List<Scene>();
            for(int i=0; i<SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if(scene.isLoaded && (scene.isDirty || forceSave) && !string.IsNullOrEmpty(scene.path))
                {
                    dirtyScenes.Add(scene);
                }
            }

            if(dirtyScenes.Count > 0)
            {
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();

                foreach(Scene scene in dirtyScenes)
                {
                    savedItems.Add(scene.name);
                    if(EnableBackups)
                    {
                        CreateSceneBackup(scene);
                    }
                }
            }

            // 3. Feedback: Console Log & Scene View Toast Notification
            if(savedItems.Count > 0)
            {
                string joinedNames = string.Join(", ", savedItems);

                if(ShowConsoleLogs)
                {
                    Debug.Log($"<b>[AutoSave]</b> Saved {joinedNames} ({reason}) at {System.DateTime.Now:HH:mm:ss}");
                }

                if(ShowSceneToast && SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.ShowNotification(new GUIContent($"Auto-Saved: {joinedNames}"), 1.5f);
                }
            }
        }

        private static void EnsureBackupFolderAndGitIgnore()
        {
            if(!Directory.Exists(BackupFolderPath))
            {
                Directory.CreateDirectory(BackupFolderPath);
            }

            // Automatically create a .gitignore inside AutoSaveBackups so git ignore backup files!
            string gitIgnorePath = Path.Combine(BackupFolderPath, ".gitignore");
            if(!File.Exists(gitIgnorePath))
            {
                File.WriteAllText(gitIgnorePath, "# Automatically generated by Smart Auto Save\n*\n");
            }
        }

        private static void CreateSceneBackup(Scene scene)
        {
            EnsureBackupFolderAndGitIgnore();

            string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupFileName = $"{scene.name}_{timeStamp}.unity";
            string destinationpath = Path.Combine(BackupFolderPath, backupFileName);

            // Copy the saved .unity scene file into our backup folder
            File.Copy(scene.path, destinationpath, overwrite: true);

            // Clean up old backups for this scene if we exceed MaxBackups
            var sceneBackups = new DirectoryInfo(BackupFolderPath).GetFiles($"{scene.name}_*.unity").OrderByDescending(f=>f.CreationTime).ToList();

            if(sceneBackups.Count > MaxBackups)
            {
                for(int i=MaxBackups; i<sceneBackups.Count; i++)
                {
                    sceneBackups[i].Delete();
                }
            }
        }

        private static void RestoreBackup(string backupFilePath)
        {
            // Find which open scene matches this backup (or fall back to the active scene)
            string backupName = Path.GetFileNameWithoutExtension(backupFilePath);
            Scene targetScene = SceneManager.GetActiveScene();

            for(int i=0; i<SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if(backupName.StartsWith(s.name + "_"))
                {
                    targetScene = s;
                    break;
                }
            }

            if(string.IsNullOrEmpty(targetScene.path))
            {
                return;
            }

            bool confirm = EditorUtility.DisplayDialog("Restore Scene Backup?", $"Are you sure you want to overwrite '{targetScene.name}' with backup:\n\n{Path.GetFileName(backupFilePath)}?", "Yes, Restore", "Cancel");

            if(confirm)
            {
                File.Copy(backupFilePath, targetScene.path, overwrite: true);
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(targetScene.path, OpenSceneMode.Single);

                if(ShowConsoleLogs)
                {
                    Debug.Log($"<b>[AutoSave]</b> Restored '{targetScene.name}' from backup: {Path.GetFileName(backupFilePath)}");
                }
            }
        }

        public static void ResetTimer()
        {
            _nextSaveTime = EditorApplication.timeSinceStartup + SaveInterval;
        }
    }
}