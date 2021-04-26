using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityBuilderAction.Input;
using UnityBuilderAction.Reporting;
using UnityBuilderAction.Versioning;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;

namespace UnityBuilderAction
{
  static class Builder
  {
    public static void BuildProject()
    {
      // Gather values from args
      var options = ArgumentsParser.GetValidatedOptions();

      // Gather values from project
      var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
      
      // Get all buildOptions from options
      BuildOptions buildOptions = BuildOptions.None;
      foreach (string buildOptionString in Enum.GetNames(typeof(BuildOptions))) {
        if (options.ContainsKey(buildOptionString)) {
          BuildOptions buildOptionEnum = (BuildOptions) Enum.Parse(typeof(BuildOptions), buildOptionString);
          buildOptions |= buildOptionEnum;
        }
      }

      // Define BuildPlayer Options
      var buildPlayerOptions = new BuildPlayerOptions {
        scenes = scenes,
        locationPathName = options["customBuildPath"],
        target = (BuildTarget) Enum.Parse(typeof(BuildTarget), options["buildTarget"]),
        options = buildOptions
      };

      // Set version for this build
      VersionApplicator.SetVersion(options["buildVersion"]);
      VersionApplicator.SetAndroidVersionCode(options["androidVersionCode"]);
      
      // Apply Android settings
      if (buildPlayerOptions.target == BuildTarget.Android)
        AndroidSettings.Apply(options);

      // Execute default AddressableAsset content build, if the package is installed.
      // Version defines would be the best solution here, but Unity 2018 doesn't support that,
      // so we fall back to using reflection instead.
      var addressableAssetSettingsType = Type.GetType(
        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings,Unity.Addressables.Editor");
      if (addressableAssetSettingsType != null)
      {
        // ReSharper disable once PossibleNullReferenceException, used from try-catch
        void CallAddressablesMethod(string methodName, object[] args) => addressableAssetSettingsType
          .GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
          .Invoke(null, args);

        try
        {
          CallAddressablesMethod("CleanPlayerContent", new object[] { null });
          CallAddressablesMethod("BuildPlayerContent", Array.Empty<object>());
        }
        catch (Exception e)
        {
          Debug.LogError($"Failed to run default addressables build:\n{e}");
        }
      }

      // Perform build
      BuildReport buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);

      // Summary
      BuildSummary summary = buildReport.summary;
      StdOutReporter.ReportSummary(summary);

      // Result
      BuildResult result = summary.result;
      StdOutReporter.ExitWithResult(result);
    }
    [PostProcessBuildAttribute(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject) {
      var options = ArgumentsParser.GetValidatedOptions();
      
      if(options["customBuildPath"].EndsWith("Android")){
        string projectFolder = Path.Combine( Application.dataPath, "../" );
        DirectoryCopy(projectFolder + "/Android", options["customBuildPath"], true);
      }
    }
    private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
    {
        Debug.Log("From: " + sourceDirName + ", To: " + destDirName);
        // Get the subdirectories for the specified directory.
        DirectoryInfo dir = new DirectoryInfo(sourceDirName);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException(
                "Source directory does not exist or could not be found: "
                + sourceDirName);
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        
        // If the destination directory doesn't exist, create it.       
        Directory.CreateDirectory(destDirName);        

        // Get the files in the directory and copy them to the new location.
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destDirName, file.Name);
            file.CopyTo(tempPath, false);
        }

        // If copying subdirectories, copy them and their contents to new location.
        if (copySubDirs)
        {
            foreach (DirectoryInfo subdir in dirs)
            {
                string tempPath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
            }
        }
    }
  }
}
