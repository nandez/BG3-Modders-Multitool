﻿/// <summary>
/// The 3d model helper.
/// </summary>
namespace bg3_modders_multitool.Services
{
    using bg3_modders_multitool.Models.GameObjectTypes;
    using HelixToolkit.Wpf.SharpDX;
    using HelixToolkit.Wpf.SharpDX.Assimp;
    using HelixToolkit.Wpf.SharpDX.Model.Scene;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;

    public static class RenderedModelHelper
    {
        /// <summary>
        /// Looks up and loads all the model geometry for the gameobject.
        /// </summary>
        /// <param name="gameObjectAttributes">The gameobject attributes to get the necessary lookup information from.</param>
        /// <param name="characterVisualBanks">The character visualbanks file lookup.</param>
        /// <param name="visualBanks">The visualbanks file lookup.</param>
        /// <returns>The list of geometry lookups.</returns>
        public static List<Dictionary<string, List<MeshGeometry3D>>> GetMeshes(List<Models.GameObjects.GameObjectAttribute> gameObjectAttributes, Dictionary<string, string> characterVisualBanks, Dictionary<string, string> visualBanks)
        {
            //var importFormats = Importer.SupportedFormats;
            //var exportFormats = HelixToolkit.Wpf.SharpDX.Assimp.Exporter.SupportedFormats;

            var gr2Files = new List<string>();

            // Check GameObject type
            var type = (FixedString)gameObjectAttributes.Single(goa => goa.Name == "Type").Value;

            // Lookup CharacterVisualBank file from CharacterVisualResourceID
            // CharacterVisualResourceID => characters, load CharacterVisualBank, then VisualBanks
            // VisualTemplate => items, load VisualBanks
            switch (type)
            {
                case "character":

                    break;
                case "item":
                case "scenery":
                case "TileConstruction":
                    var visualTemplate = (FixedString)gameObjectAttributes.SingleOrDefault(goa => goa.Name == "VisualTemplate")?.Value;
                    if (visualTemplate != null)
                    {
                        visualBanks.TryGetValue(visualTemplate, out string file);
                        // TODO - Possible that a visual template could link to other templates?
                        if (file != null)
                        {
                            var visualResource = LoadVisualResource(FileHelper.GetPath(file), visualTemplate);
                            if (visualResource != null)
                                gr2Files.Add(visualResource);
                        }
                    }
                    break;
                default:
                    break;
            }

            
            // Scan through resources to find matching Resource
            // Scan Resource Slots for VisualResource IDs
            // Loop through IDs to find VisualBank Resource files, make distinct
            // Locate matching VisualBank Resources, pull SourceFile value

            // GameObject (Volo)
            //  <attribute id="MapKey" type="FixedString" value="2af25a85-5b9a-4794-85d3-0bd4c4d262fa" />
            //  <attribute id="CharacterVisualResourceID" type="FixedString" value="f8103934-8d3d-cbd9-5cf6-1a8951b98e93" />
            // CharacterVisual Resource
            //  <attribute id="ID" type="FixedString" value="f8103934-8d3d-cbd9-5cf6-1a8951b98e93" />
            //  <attribute id="BaseVisual" type="FixedString" value="3773c64c-c5a9-9baf-1b85-6bee029ee044" /> Asset Id for human male base
            //  <attribute id="BodySetVisual" type="FixedString" value="38cee76d-1a75-4419-9293-52e47fda65e9" /> Asset Id for human male body A
            //  Slots list
            //      <attribute id="VisualResource" type="FixedString" value="44e7769e-bc14-8c16-40d0-8b576ceddcb1" />  Volo head (Shared\Public\Shared\Content\Assets\Characters\Humans\Heads\[PAK]_HUM_M_Head_Volo\_merged.lsx)
            //      VisualBank Resource
            //          <attribute id="ID" type="FixedString" value="44e7769e-bc14-8c16-40d0-8b576ceddcb1" />
            //          <attribute id="SourceFile" type="LSWString" value="Generated/Public/Shared/Assets/Characters/_Anims/Humans/_Male/Resources/HUM_M_NKD_Head_Volo.GR2" />
            //          child objects - match name to get materialid per part
            //      <attribute id="VisualResource" type="FixedString" value="c60465a9-71bd-b436-c837-7dfadf7edf1c" /> Volo bar shirt (Shared\Public\Shared\Content\Assets\Characters\Humans\[PAK]_Male_Clothing\_merged.lsx)
            //      VisualBank Resource
            //          <attribute id="ID" type="FixedString" value="c60465a9-71bd-b436-c837-7dfadf7edf1c" />
            //          <attribute id="SourceFile" type="LSWString" value="Generated/Public/Shared/Assets/Characters/_Models/Humans/Resources/HUM_M_CLT_Bard_Shirt_A.GR2" />
            //          child objects - match name to get materialid per part

            // Get model for loaded object (.GR2)
            //var files = new string[] { 
            //    @"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated/Public/Shared/Assets/Characters/_Anims/Humans/_Male/Resources/HUM_M_NKD_Head_Volo",
            //    @"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated/Public/Shared/Assets/Characters/_Models/Humans/Resources/Beard_HUM_Volo",
            //    @"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated/Public/Shared/Assets/Characters/_Models/Humans/Resources/HUM_M_CLT_Headwear_MuffinHat_Volo",
            //    //@"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated/Public/Shared/Assets/Characters/_Models/Humans/Resources/HUM_M_CLT_Bard_Shirt_A",
            //    //@"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated/Public/Shared/Assets/Characters/_Models/Humans/Resources/HUM_M_CLT_MiddleClass_E_1_Lowerbody",
            //    //@"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated\Public\Shared\Assets\Characters\_Models\_Creatures\Automaton\Resources\AUTOMN_M_Body_A", // too big
            //    //@"J:\BG3\bg3-modders-multitool\bg3-modders-multitool\bg3-modders-multitool\bin\x64\Debug\UnpackedData\Models\Generated\Public\Shared\Assets\Characters\_Models\_Creatures\Elementals\Resources\ELEM_Mud_Body_A" // too big
            //};

            var geometryGroup = new List<Dictionary<string, List<MeshGeometry3D>>>();

            foreach(var gr2File in gr2Files)
            {
                var geometry = GetMesh(gr2File);
                if(geometry != null)
                    geometryGroup.Add(geometry);
            }

            return geometryGroup;
        }

        /// <summary>
        /// Gets the model geometry from a given .GR2 file and organizes it by LOD level.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns>The geometry lookup table.</returns>
        public static Dictionary<string, List<MeshGeometry3D>> GetMesh(string filename)
        {
            var file = LoadFile(filename);
            if (file == null)
                return null;

            // Gather meshes
            var meshes = file.Root.Items.Where(x => x.Items.Any(y => y as MeshNode != null)).ToList();
            // Group meshes by lod
            var meshGroups = meshes.GroupBy(mesh => mesh.Name.Split('_').Last()).ToList();
            var geometryLookup = new Dictionary<string, List<MeshGeometry3D>>();
            foreach (var meshGroup in meshGroups)
            {
                var geometryList = new List<MeshGeometry3D>();

                // Selecting body first
                foreach(var mesh in meshGroup)
                {
                    var meshNode = mesh.Items.Last() as MeshNode;
                    var meshGeometry = meshNode.Geometry as MeshGeometry3D;
                    meshGeometry.Normals = meshGeometry.CalculateNormals();
                    geometryList.Add(meshGeometry);
                }
                geometryLookup.Add(meshGroup.Key, geometryList);
            }
            return geometryLookup;
        }

        /// <summary>
        /// Converts a given .GR2 file to a .dae file for rendering and further conversion.
        /// </summary>
        /// <param name="filename">The file name.</param>
        /// <returns>The .dae converted model file.</returns>
        private static HelixToolkitScene LoadFile(string filename)
        {
            var dae = $"{filename}.dae";

            if (!File.Exists(dae))
            {
                GeneralHelper.WriteToConsole($"Converting model to .dae for rendering...\n");
                var divine = $" -g \"bg3\" --action \"convert-model\" --output-format \"dae\" --source \"\\\\?\\{filename}.GR2\" --destination \"\\\\?\\{dae}\" -l \"all\"";
                var process = new Process();
                var startInfo = new ProcessStartInfo
                {
                    FileName = Properties.Settings.Default.divineExe,
                    Arguments = divine,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
                GeneralHelper.WriteToConsole(process.StandardOutput.ReadToEnd());
                GeneralHelper.WriteToConsole(process.StandardError.ReadToEnd());
            }
            var importer = new Importer();
            // Update material here?
            var file = importer.Load(dae);
            if (file == null && File.Exists(dae))
            {
                GeneralHelper.WriteToConsole("Fixing vertices...\n");
                var xml = XDocument.Load(dae);
                var geometryList = xml.Descendants().Where(x => x.Name.LocalName == "geometry").ToList();
                foreach (var lod in geometryList)
                {
                    var vertexId = lod.Descendants().Where(x => x.Name.LocalName == "vertices").Select(x => x.Attribute("id").Value).First();
                    var vertex = lod.Descendants().Single(x => x.Name.LocalName == "input" && x.Attribute("semantic").Value == "VERTEX");
                    vertex.Attribute("source").Value = $"#{vertexId}";
                }
                xml.Save(dae);
                GeneralHelper.WriteToConsole("Model conversion complete!\n");
                file = importer.Load(dae);
            }
            importer.Dispose();
            return file;
        }

        /// <summary>
        /// Finds the visualresource sourcefile for an object.
        /// </summary>
        /// <param name="filename">The file to search.</param>
        /// <param name="id">The id to match.</param>
        /// <returns>The .GR2 sourcefile.</returns>
        private static string LoadVisualResource(string filename, string id)
        {
            var xml = XDocument.Load(filename);
            var visualResource = xml.Descendants().Where(x => x.Name.LocalName == "node" && x.Attribute("id").Value == "Resource" && x.Elements("attribute").Single(a => a.Attribute("id").Value == "ID").Attribute("value").Value == id).First();
            var gr2File = visualResource.Elements("attribute").SingleOrDefault(a => a.Attribute("id").Value == "SourceFile")?.Attribute("value").Value;
            if (gr2File == null)
                return null;
            gr2File = gr2File.Replace(".GR2", string.Empty);
            return FileHelper.GetPath($"Models\\{gr2File}");
        }
    }
}
