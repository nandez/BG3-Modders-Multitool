﻿/// <summary>
/// Helper for processing drag and drop events
/// </summary>
namespace bg3_modders_multitool.Services
{
    using Alphaleonis.Win32.Filesystem;
    using bg3_modders_multitool.Models;
    using LSLib.LS.Enums;
    using LSLib.LS;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO.Compression;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Xml;
    using bg3_modders_multitool.Properties;
    using bg3_modders_multitool.Views.Utilities;
    using System.Xml.Linq;
    using System.Linq;
    using bg3_modders_multitool.Views.Other;

    public static class DragAndDropHelper
    {
        /// <summary>
        /// Path to the temp directory to use.
        /// </summary>
        public static string TempFolder = Path.GetTempPath() + "BG3ModPacker";

        /// <summary>
        /// Generates a list of meta.lsx files, representing the mods present.
        /// </summary>
        /// <param name="pathlist">The list of directories within the \Mods folder.</param>
        /// <returns></returns>
        public static List<string> GetMetalsxList(string[] pathlist)
        {
            var metaList = new List<string>();
            foreach (string mod in pathlist)
            {
                foreach (string file in Directory.GetFiles(mod))
                {
                    if (Path.GetFileName(file).Equals("meta.lsx"))
                    {
                        metaList.Add(file);
                        var modRoot = new FileInfo(file).Directory.Parent.Parent.Parent.FullName;
                        GeneralHelper.WriteToConsole(Properties.Resources.MetaLsxFound, mod.Replace(modRoot + "\\", string.Empty));
                    }
                }
            }

            if (metaList.Count == 0)
            {
                foreach (string mod in pathlist)
                {
                    var invokeResult = Application.Current.Dispatcher.Invoke(() => {
                        var addMeta = new AddMissingMetaLsx(mod);
                        var result = addMeta.ShowDialog();
                        if (result == true)
                        {
                            if (!string.IsNullOrEmpty(addMeta.MetaPath))
                                metaList.Add(addMeta.MetaPath);
                        }
                        return result;
                    });
                }

                if(metaList.Count == 0)
                {
                    // meta.lsx not found, discontinue
                    throw new System.IO.FileNotFoundException(Properties.Resources.MetaLsxNotFound);
                }
            }
            return metaList;
        }

        /// <summary>
        /// Packs the mod into a .pak
        /// </summary>
        /// <param name="fullpath">The full path for the source folder.</param>
        /// <param name="destination">The destination path and mod name.</param>
        public static void PackMod(string fullpath, string destination)
        {
            Directory.CreateDirectory(TempFolder);
            var packageOptions = new PackageCreationOptions() { 
                Version = Game.BaldursGate3.PAKVersion(),
                Priority = 21,
                Compression = CompressionMethod.LZ4
            };
            try
            {
                new Packager().CreatePackage(destination, fullpath, packageOptions);
            }
            catch (Exception ex)
            {
                GeneralHelper.WriteToConsole(Properties.Resources.FailedToPackMod, ex.Message);
            }
        }

        /// <summary>
        /// Creates the metadata info.json file.
        /// </summary>
        /// <param name="metaList">The list of meta.lsx file paths.</param>
        /// <returns>Whether or not info.json was generated</returns>
        public static bool GenerateInfoJson(Dictionary<string, List<string>> metaList)
        {
            var info = new InfoJson {
                Mods = new List<MetaLsx>()
            };
            var created = DateTime.Now;
            
            foreach(KeyValuePair<string, List<string>> modGroup in metaList)
            {
                var mods = new List<MetaLsx>();
                foreach (var meta in modGroup.Value)
                {
                    var metadata = ReadMeta(meta, created, modGroup);
                    mods.Add(metadata);
                    GeneralHelper.WriteToConsole(Properties.Resources.MetadataCreated, metadata.Name);
                }
                info.Mods.AddRange(mods);
            }

            if (info.Mods.Count == 0)
                return false;

            // calculate md5 hash of .pak(s)
            using (var md5 = MD5.Create())
            {
                var paks = Directory.GetFiles(TempFolder);
                var pakCount = 1;
                foreach (var pak in paks)
                {
                    byte[] contentBytes = File.ReadAllBytes(pak);
                    if (pakCount == paks.Length)
                        md5.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
                    else
                        md5.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
                    pakCount++;
                }
                info.MD5 = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();
            }

            var json = JsonConvert.SerializeObject(info);
            File.WriteAllText(TempFolder + @"\info.json", json);
            GeneralHelper.WriteToConsole(Properties.Resources.InfoGenerated);
            return true;
        }

        public static MetaLsx ReadMeta(string meta, DateTime? created = null, KeyValuePair<string, List<string>>? modGroup = null)
        {
            // generate info.json section
            XmlDocument doc = new XmlDocument();
            doc.Load(meta);

            var moduleInfo = doc.SelectSingleNode("//node[@id='ModuleInfo']");
            var metadata = new MetaLsx
            {
                Author = moduleInfo.SelectSingleNode("attribute[@id='Author']")?.Attributes["value"].InnerText,
                Name = moduleInfo.SelectSingleNode("attribute[@id='Name']")?.Attributes["value"].InnerText,
                Description = moduleInfo.SelectSingleNode("attribute[@id='Description']")?.Attributes["value"].InnerText,
                Version = moduleInfo.SelectSingleNode("attribute[@id='Version']")?.Attributes["value"].InnerText,
                Folder = moduleInfo.SelectSingleNode("attribute[@id='Folder']")?.Attributes["value"].InnerText,
                UUID = moduleInfo.SelectSingleNode("attribute[@id='UUID']")?.Attributes["value"].InnerText,
                Created = created,
                Group = modGroup?.Key ?? string.Empty,
                Dependencies = new List<ModuleShortDesc>()
            };

            var dependencies = doc.SelectSingleNode("//node[@id='Dependencies']");
            if (dependencies != null)
            {
                var moduleDescriptions = dependencies.SelectNodes("node[@id='ModuleShortDesc']");
                foreach (XmlNode moduleDescription in moduleDescriptions)
                {
                    var depInfo = new ModuleShortDesc
                    {
                        Name = moduleDescription.SelectSingleNode("attribute[@id='Name']").Attributes["value"].InnerText,
                        Version = moduleDescription.SelectSingleNode("attribute[@id='Version']").Attributes["value"].InnerText,
                        Folder = moduleDescription.SelectSingleNode("attribute[@id='Folder']").Attributes["value"].InnerText,
                        UUID = moduleDescription.SelectSingleNode("attribute[@id='UUID']").Attributes["value"].InnerText
                    };
                    metadata.Dependencies.Add(depInfo);
                }
            }
            return metadata;
        }

        /// <summary>
        /// Generates a .zip file containing the .pak(s) and info.json (contents of the temp directory)
        /// </summary>
        /// <param name="fullpath">The full path to the directory location to create the .zip.</param>
        /// <param name="name">The name to use for the .zip file.</param>
        public static void GenerateZip(string fullpath, string name)
        {
            // save zip next to folder that was dropped
            var parentDir = Directory.GetParent(fullpath);
            var zip = $"{parentDir}\\{name}.zip";
            if (File.Exists(zip))
            {
                File.Delete(zip);
            }
            ZipFile.CreateFromDirectory(TempFolder, zip);
            GeneralHelper.WriteToConsole(Properties.Resources.ZipCreated, name);
        }

        /// <summary>
        /// Cleans all the files out of the temp directory used.
        /// </summary>
        /// <param name="writeToConsole">Whether or not to write to the console</param>
        public static void CleanTempDirectory(bool writeToConsole = true)
        {
            // cleanup temp folder
            DirectoryInfo di = new DirectoryInfo(TempFolder);

            foreach (FileInfo file in di.GetFiles()) file.Delete();
            foreach (DirectoryInfo subDirectory in di.GetDirectories()) subDirectory.Delete(true);

            if(writeToConsole)
                GeneralHelper.WriteToConsole(Properties.Resources.TempFilesCleaned);
        }

        /// <summary>
        /// Process the file/folder drop.
        /// </summary>
        /// <param name="data">Drop data, should be a folder</param>
        /// <returns>Success</returns>
        public async static Task ProcessDrop(IDataObject data)
        {
            await Task.Run(() => {
                try
                {
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        var fileDrop = data.GetData(DataFormats.FileDrop, true);
                        if (fileDrop is string[] filesOrDirectories && filesOrDirectories.Length > 0)
                        {
                            Directory.CreateDirectory(TempFolder);
                            CleanTempDirectory();
                            foreach (string fullPath in filesOrDirectories)
                            {
                                // Only accept directory
                                if (Directory.Exists(fullPath))
                                {
                                    var metaList = new Dictionary<string, List<string>>();
                                    var dirInfo = new DirectoryInfo(fullPath);
                                    var dirName = dirInfo.Name;
                                    //GeneralHelper.WriteToConsole(Properties.Resources.DirectoryName, dirName);
                                    var metaFiles = dirInfo.GetFiles("meta.lsx", System.IO.SearchOption.AllDirectories);
                                    var modsFolders = dirInfo.GetDirectories("Mods", System.IO.SearchOption.AllDirectories);

                                    if (Directory.Exists(fullPath + "\\Mods"))
                                    {
                                        // single mod directory
                                        metaList.Add(Guid.NewGuid().ToString(), ProcessMod(fullPath, dirName));
                                    }
                                    else if(modsFolders.Length > 0)
                                    {
                                        // multiple mod directories?
                                        foreach (string dir in Directory.GetDirectories(fullPath))
                                        {
                                            metaList.Add(Guid.NewGuid().ToString(), ProcessMod(dir, new DirectoryInfo(dir).Name));
                                        }
                                    }
                                    else
                                    {
                                        GeneralHelper.WriteToConsole(Properties.Resources.NoModsFolderFound);
                                        CleanTempDirectory();
                                        return;
                                    }

                                    if (Properties.Settings.Default.pakToMods)
                                    {
                                        var modsFolder = $"{Properties.Settings.Default.gameDocumentsPath}\\Mods";
                                        if(Directory.Exists(modsFolder))
                                        {
                                            File.Move($"{TempFolder}\\{dirName}.pak", $"{modsFolder}\\{dirName}.pak", MoveOptions.ReplaceExisting);
                                            GeneralHelper.WriteToConsole(Properties.Resources.PakModedToMods, dirName);
                                        }
                                    }
                                    else
                                    {
                                        if(GenerateInfoJson(metaList))
                                            GenerateZip(fullPath, dirName);
                                    }
                                    CleanTempDirectory();
                                }
                                else if(File.Exists(fullPath))
                                {
                                    var task = Application.Current.Dispatcher.Invoke(() => {
                                        var vm = App.Current.MainWindow.DataContext as ViewModels.MainWindow;
                                        return vm.Unpacker.UnpackPakFiles(new List<string> { fullPath }, false);
                                    });
                                    task.Wait();
                                }
                                else
                                {
                                    GeneralHelper.WriteToConsole(Properties.Resources.FailedToProcessWorkspace);
                                }
                            }
                        }
                    }
                }
                catch(System.IO.FileNotFoundException ex)
                {
                    GeneralHelper.WriteToConsole(ex.Message);
                    CleanTempDirectory();
                }
                catch (Exception ex)
                {
                    GeneralHelper.WriteToConsole(Properties.Resources.GeneralError, ex.Message, ex.StackTrace);
                    CleanTempDirectory();
                }
            });
        }

        /// <summary>
        /// Builds a pack from converted files.
        /// </summary>
        /// <param name="path">The mod root path.</param>
        /// <returns>The new mod build directory.</returns>
        private static (string ModBuild, List<string> MetaFile) BuildPack(string path)
        {
            var modName = new DirectoryInfo(path).Name;
            var modDir = $"{TempFolder}\\{modName}";

            var metaList = CheckAndCreateMeta(path, modName);
            if(metaList.Count == 0)
                return (null, null);

            CopyWorkingFilesToTempDir(path, modDir);

            // TODO - add option to turn off
            if(!LintLsxFiles(modDir))
                return (null, null);

            ConsolidateSubfolders(modDir);

            ProcessLsxMerges(modDir);

            ProcessStatsGeneratedDataSubfiles(modDir);

            ConvertFiles(modDir);

            return (modDir, metaList);
        }

        #region Packing Steps
        /// <summary>
        /// Checks for meta.lsx and asks to create one if one doesn't exist
        /// </summary>
        /// <param name="path">The mod path</param>
        /// <param name="modName">The mod name</param>
        /// <returns>The list of meta.lsx files found</returns>
        private static List<string> CheckAndCreateMeta(string path, string modName)
        {
            // Create meta if it does not exist
            var modsPath = Path.Combine(path, "Mods");
            var pathList = Directory.GetDirectories(modsPath);
            if (pathList.Length == 0)
            {
                var newModsPath = Path.Combine(modsPath, modName);
                Directory.CreateDirectory(newModsPath);
                pathList = new string[] { newModsPath };
            }
            var metaList = GetMetalsxList(pathList);
            return metaList;
        }
        /// <summary>
        /// Copies the working files to the temp directory for merging and conversion
        /// </summary>
        /// <param name="path">The mod root path</param>
        /// <param name="modDir">The working path directory to copy to</param>
        private static void CopyWorkingFilesToTempDir(string path, string modDir)
        {
            var fileList = Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
            foreach (var file in fileList)
            {
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(extension))
                {
                    // copy to temp dir
                    var fileParent = file.Replace(path, string.Empty);
                    var mod = $"{modDir}{fileParent}";
                    var modParent = new DirectoryInfo(mod).Parent.FullName;

                    // check if matching file for .lsf exists as .lsf.lsx and ignore if yes
                    if (new FileInfo(file).Directory.GetFiles(fileName + "*").Length == 1)
                    {
                        Directory.CreateDirectory(modParent);
                        File.Copy(file, mod, true);
                    }
                }
            }
        }

        /// <summary>
        /// Checks the lsx files for xml errors
        /// XML formatting errors
        /// Missing required node components
        /// Unmatched UUIDs
        /// </summary>
        /// <param name="directory">The directory to scan</param>
        /// <returns>Whether or not the directory is error free</returns>
        private static bool LintLsxFiles(string directory)
        {
            var dirInfo = new DirectoryInfo(directory);
            if (dirInfo.Exists)
            {
                var files = dirInfo.GetFiles("*.lsx", System.IO.SearchOption.AllDirectories);
                var errors = new List<LintingError>();
                foreach(var file in files)
                {
                    try
                    {
                        using(var xml = XmlReader.Create(file.FullName))
                        {
                            while(!xml.EOF)
                            {
                                xml.Read();
                                IXmlLineInfo xmlInfo = xml as IXmlLineInfo;
                                var line = xmlInfo?.LineNumber;
                                var error = string.Empty;
                                if (xml.Name == "attribute" && xml.NodeType == XmlNodeType.Element)
                                {
                                    var id = xml.GetAttribute("id");
                                    var type = xml.GetAttribute("type");
                                    var value = xml.GetAttribute("value");
                                    var handle = xml.GetAttribute("handle");
                                    if (id == null)
                                        error += string.Format(Properties.Resources.AttributeMissing, "'id'");
                                    if (type == null)
                                        error += string.Format(Properties.Resources.AttributeMissing, "'type'");
                                    if (value == null && handle == null)
                                        error += string.Format(Properties.Resources.AttributeMissing, "'value' || 'handle'");
                                    
                                }
                                if(xml.Name == "node" && xml.NodeType == XmlNodeType.Element)
                                {
                                    var id = xml.GetAttribute("id");
                                    if (id == null)
                                        error += string.Format(Properties.Resources.NodeAttributeMissing, "id");
                                }
                                if (!string.IsNullOrEmpty(error))
                                {
                                    error = (string.Format(Properties.Resources.ErrorLine, line) + error);
                                    error = error.Substring(0, error.Length - 2); // remove last next line char
                                    errors.Add(new LintingError(file.FullName.Replace(TempFolder + "\\", string.Empty), error, LintingErrorType.AttributeMissing));
                                }
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        errors.Add(new LintingError(file.FullName.Replace(TempFolder + "\\", string.Empty), ex.Message, LintingErrorType.Xml));
                    }
                }
                if (errors.Count > 0)
                {
                    GeneralHelper.WriteToConsole(Properties.Resources.ErrorsFoundPacking);
                    Application.Current.Dispatcher.Invoke(() => {
                        var popup = new FileLintingWindow(errors);
                        return popup.ShowDialog();
                    });

                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Moves subfolder files up to the main level
        /// </summary>
        /// <param name="directory">The mod workspace directory</param>
        private static void ConsolidateSubfolders(string directory)
        {
            var modNameDirs = new DirectoryInfo(Path.Combine(directory, "Public"));
            if (modNameDirs.Exists)
            {
                var paths = modNameDirs.GetDirectories("*", System.IO.SearchOption.TopDirectoryOnly);
                foreach (var modName in paths)
                {
                    foreach (var dir in new string[] { "MultiEffectInfos" })
                    {
                        var path = Path.Combine(directory, "Public", modName.Name, dir);
                        var dirInfo = new DirectoryInfo(path);
                        if (dirInfo.Exists)
                        {
                            var files = dirInfo.GetFiles("*", System.IO.SearchOption.AllDirectories);
                            foreach(var file in files)
                            {
                                var newFile = Path.Combine(path, file.Name);
                                if(newFile != file.FullName)
                                {
                                    if (File.Exists(newFile))
                                    {
                                        GeneralHelper.WriteToConsole(Properties.Resources.DuplicateFileFoundReplacing, file.Name);
                                    }
                                    File.Move(file.FullName, newFile, MoveOptions.ReplaceExisting);
                                }
                            }

                            foreach (var delDir in dirInfo.GetDirectories())
                            {
                                delDir.Delete(true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Concatenates .lsx files prior to conversion to .lsf
        /// </summary>
        /// <param name="directory">The mod workspace directory</param>
        private static void ProcessLsxMerges(string directory)
        {
            var modNameDirs = new DirectoryInfo(Path.Combine(directory, "Public"));
            if(modNameDirs.Exists)
            {
                var paths = modNameDirs.GetDirectories("*", System.IO.SearchOption.TopDirectoryOnly);
                foreach (var modName in paths)
                {
                    foreach (var dir in new string[] { "Progressions", "Races", "ClassDescriptions", "ActionResourceDefinitions", "Lists", "RootTemplates" })
                    {
                        var isRootTemplate = dir == "RootTemplates";
                        var isList = dir == "Lists";

                        var path = Path.Combine(directory, "Public", modName.Name, dir);
                        var dirInfo = new DirectoryInfo(path);
                        if (dirInfo.Exists)
                        {
                            var files = dirInfo.GetFiles("*.lsx", System.IO.SearchOption.AllDirectories);
                            var fileGroups = isRootTemplate ? files.GroupBy(f => dir) : files.GroupBy(f => f.Name.Split('.').Reverse().Skip(1).First());
                            foreach (var fileGroup in fileGroups)
                            {
                                var template = FileHelper.LoadFileTemplate("LsxBoilerplate.lsx");
                                var xml = XDocument.Parse(template);
                                xml.AddFirst(new XComment(Properties.Resources.GeneratedWithDisclaimer));
                                if (isRootTemplate)
                                {
                                    xml.Descendants("region").Single().Attribute("id").Value = "Templates";
                                    xml.Descendants("node").Single().Attribute("id").Value = "Templates";
                                }
                                else
                                {
                                    xml.Descendants("region").Single().Attribute("id").Value = fileGroup.Key;
                                }

                                var children = xml.Descendants("children").Single();
                                foreach (var file in fileGroup)
                                {
                                    if(FileHelper.CanConvertToLsx(Path.GetFileNameWithoutExtension(file.Name)) || !isRootTemplate)
                                    {
                                        using (System.IO.StreamReader reader = new System.IO.StreamReader(file.FullName))
                                        {
                                            var contents = XDocument.Parse(reader.ReadToEnd(), LoadOptions.PreserveWhitespace);
                                            var contentChildren = contents.Descendants("children").First().Elements().ToList();
                                            foreach (var child in contentChildren)
                                            {
                                                children.Add(child);
                                            }
                                        }
                                    }
                                    file.Delete();
                                }
                                var fileName = isRootTemplate ? "_merged" : fileGroup.Key;
                                xml.Save($"{path}\\{fileName}{(isRootTemplate ? ".lsf" : "")}.lsx");
                            }

                            foreach (var delDir in dirInfo.GetDirectories())
                            {
                                delDir.Delete(true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Concatenate Stats\Generated\Data sub directory files
        /// </summary>
        /// <param name="directory">The mod workspace directory</param>
        private static void ProcessStatsGeneratedDataSubfiles(string directory)
        {
            var modNameDirs = new DirectoryInfo(Path.Combine(directory, "Public"));
            if(modNameDirs.Exists )
            {
                var paths = modNameDirs.GetDirectories("*", System.IO.SearchOption.TopDirectoryOnly);
                foreach (var modName in paths)
                {
                    var statsGeneratedDataDir = $"{directory}\\Public\\{modName.Name}\\Stats\\Generated\\Data";
                    var sgdInfo = new DirectoryInfo(statsGeneratedDataDir);
                    if (sgdInfo.Exists)
                    {
                        var sgdDirs = sgdInfo.GetDirectories();
                        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        foreach (var dir in sgdDirs)
                        {
                            var files = dir.GetFiles("*.txt", System.IO.SearchOption.AllDirectories);
                            var fileName = $"{dir.Parent.FullName}\\__MT_GEN_{dir.Name}_{now}.txt";
                            using (System.IO.FileStream fs = new System.IO.FileStream(fileName, System.IO.FileMode.Append, System.IO.FileAccess.Write))
                            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fs))
                            {
                                sw.WriteLine($"// ==== {Properties.Resources.GeneratedWithDisclaimer} ====");
                                foreach (var file in files)
                                {
                                    sw.WriteLine($"// === {file.Name} ===");
                                    using (var input = new System.IO.StreamReader(file.FullName))
                                    {
                                        sw.WriteLine(input.ReadToEnd());
                                    }
                                }
                            }
                            Directory.Delete(dir.FullName, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Converts all convertable files to their compressed version recursively
        /// </summary>
        /// <param name="tempPath">The mod temp path location</param>
        private static void ConvertFiles(string tempPath)
        {
            var fileList = Directory.GetFiles(tempPath, "*", System.IO.SearchOption.AllDirectories);
            foreach (var file in fileList)
            {
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file);
                var conversionFile = fileName.Replace(extension, string.Empty);
                var secondExtension = Path.GetExtension(conversionFile);

                var validExtension = FileHelper.ConvertableLsxResources.Contains(secondExtension) || secondExtension == ".loca";

                var mod = $"{new FileInfo(file).Directory}\\{fileName}";
                var modParent = new DirectoryInfo(mod).Parent.FullName;
                if(validExtension)
                {
                    FileHelper.Convert(file, secondExtension.Remove(0, 1), $"{modParent}\\{conversionFile}");
                    File.Delete(file);
                }
            }
        }
        #endregion

        /// <summary>
        /// Process mod for packing.
        /// </summary>
        /// <param name="path">The path to process.</param>
        /// <param name="dirName">The name of the parent directory.</param>
        /// <returns></returns>
        private static List<string> ProcessMod(string path, string dirName)
        {
            // Clean out temp folder
            var tempFolder = new DirectoryInfo(TempFolder);
            foreach (FileInfo file in tempFolder.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in tempFolder.GetDirectories())
            {
                dir.Delete(true);
            }

            // Pack mod
            var destination =  $"{TempFolder}\\{dirName}.pak";
            //GeneralHelper.WriteToConsole(Resources.Destination, destination);
            GeneralHelper.WriteToConsole(Resources.AttemptingToPack);
            var build = BuildPack(path);
            if(build.ModBuild != null)
            {
                PackMod(build.ModBuild, destination);
                Directory.Delete(build.ModBuild, true);
                return build.MetaFile;
            }
            return new List<string>();
        }
    }
}
