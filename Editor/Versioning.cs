using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;
using System;

namespace BrandLab360.Packages
{
    public static class Versioning
    {
        private static string[] packageJson;
        private static int[] currentVersion = new int[3];
        private static int[] newVersion = new int[3];
        private static string currentVersionStr = "0.0.0";
        private static string newVersionStr = "0.0.0";

        public static void GetPackageJson(string packagePath)
        {
            var path = Directory.GetCurrentDirectory() + "\\Assets\\" + packagePath;
            var fullPath = Path.Combine(path, "package.json");

            string[] lines = File.ReadAllLines(fullPath);

            packageJson = lines;
        }

        public static string[] GetNewPackageVersion()
        {
            string[] newPackageJson = new string[packageJson.Length];

            packageJson.CopyTo(newPackageJson, 0);

            for (int i = newPackageJson.Length - 1; i > 0; i--)
            {
                if (newPackageJson[i].Contains("version"))
                {
                    newPackageJson[i] = "	\"version\": \"" + newVersionStr + "\",";
                }
            }

            return newPackageJson;
        }

        public static string GetInitialVersion()
        {
            int[] output = new int[3];

            output[0] = 1;
            output[1] = 0;
            output[2] = 0;

            var outputStr = getVersionString(output);

            currentVersionStr = outputStr;

            return outputStr;
        }

        public static string GetCurrentVersion(string packagePath)
        {
            if (packageJson == null || packageJson.Length == 0)
            {
                GetPackageJson(packagePath);
            }

            int[] output = new int[3];

            output[0] = 0;
            output[1] = 0;
            output[2] = 0;

            foreach (var line in packageJson)
            {
                if (line.Contains("version"))
                {
                    var splitArr = line.Split(':');
                    if (splitArr.Length != 2) return null;

                    var finalStr = splitArr[1].Trim();
                    finalStr = finalStr.Substring(1);
                    finalStr = finalStr.Replace('\"', ' ');
                    finalStr = finalStr.Replace(',', ' ');
                    finalStr = finalStr.Trim();

                    var verArr = finalStr.Split('.');
                    if (verArr.Length < 2) return null;

                    output[0] = int.Parse(verArr[0]);
                    output[1] = int.Parse(verArr[1]);
                    output[2] = int.Parse(verArr[2]);
                }
            }

            currentVersion = output;

            var outputStr = getVersionString(output);

            currentVersionStr = outputStr;

            return outputStr;
        }

        public static string GetNewVersion(UpdateMode updateMode, string packagePath)
        {
            if (packageJson == null || packageJson.Length == 0)
            {
                GetPackageJson(packagePath);
            }

            int[] output = new int[3];

            output[0] = currentVersion[0];
            output[1] = currentVersion[1];
            output[2] = currentVersion[2];

            if (updateMode == UpdateMode.Release)
            {
                output[0]++;
                output[1] = 0;
                output[2] = 0;
            }
            else if (updateMode == UpdateMode.Update)
            {
                output[1]++;
                output[2] = 0;
            }
            else
            {
                output[2]++;
            }

            newVersion = output;

            var outputStr = getVersionString(output);

            newVersionStr = outputStr;

            return outputStr;
        }

        public static void SetDirty()
        {
            if (packageJson != null && packageJson.Length > 0)
            {
                Array.Clear(packageJson, 0, packageJson.Length);
                packageJson = null;
            }
        }

        private static string getVersionString(int[] version)
        {
            string output = "";
            for (int i = 0; i < version.Length; i++)
            {
                output += version[i].ToString();
                if (i < version.Length - 1)
                {
                    output += '.';
                }
            }

            return output;
        }
    }

    public enum UpdateMode { Initial, Release, Update, Patch };
}