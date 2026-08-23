using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Points every <c>.glb</c> under <c>Assets/Models</c> at glTFast's importer.
    ///
    /// <para><b>Why this is needed at all.</b> Installing <c>com.unity.cloud.gltfast</c> does
    /// not make it the importer for <c>.glb</c>. Its <c>GltfImporter</c> is declared
    /// <c>[ScriptedImporter(1, null, overrideExts: new[] { "gltf", "glb" })]</c> unless the
    /// project defines <c>ENABLE_DEFAULT_GLB_IMPORTER</c> — an *override* importer, which
    /// Unity registers but does not apply. Until something opts each asset in, a <c>.glb</c>
    /// keeps <c>DefaultImporter</c> and Unity sees an opaque binary blob rather than a
    /// GameObject. The symptom is baffling: the package is installed, the file is right there,
    /// and instantiating it fails with "not a GameObject".</para>
    ///
    /// <para><b>Why per-asset rather than the define.</b> Adding
    /// <c>ENABLE_DEFAULT_GLB_IMPORTER</c> to Scripting Define Symbols would also work and is
    /// one setting instead of N assets. It was not chosen because it changes how the project
    /// treats a whole file type globally and silently — a model dropped in years from now
    /// would import differently with no record of why. An explicit, re-runnable opt-in leaves
    /// the reason in the repository. Flip to the define if the project ever standardises on
    /// glTF as its model format.</para>
    ///
    /// <para><b>Re-run it after adding models.</b> The override is stored in the asset's
    /// <c>.meta</c>, so it survives, but a newly added <c>.glb</c> starts without one.
    /// Deleting a <c>.meta</c> also loses it — and regenerating gives a DefaultImporter again,
    /// which looks exactly like the package having failed to install.</para>
    ///
    /// <para>Reflection rather than a direct reference: <c>GLTFast.Editor.GltfImporter</c> is
    /// <c>internal</c> to its assembly, so it cannot be named in a <c>typeof</c> here, and
    /// <c>AssetDatabase.SetImporterOverride</c> is generic over it. This also keeps the whole
    /// file compiling when the package is absent, which matters because a compile error in an
    /// Editor script would take the rest of the tooling down with it.</para>
    /// </summary>
    public static class GlbImporterSetup
    {
        const string ModelRoot = "Assets/Models";
        const string ImporterTypeName = "GLTFast.Editor.GltfImporter";

        [MenuItem("Archivist/Models · Use glTFast Importer")]
        public static void Apply()
        {
            Type importer = FindImporter();
            if (importer == null)
            {
                Debug.LogError(
                    $"[GlbImporterSetup] {ImporterTypeName} not found. Is " +
                    "com.unity.cloud.gltfast installed and compiled?");
                return;
            }

            MethodInfo generic = SetterFor(importer);
            if (generic == null)
            {
                Debug.LogError("[GlbImporterSetup] AssetDatabase.SetImporterOverride<T>(string) " +
                               "not found on this Unity version.");
                return;
            }

            List<string> models = Models();
            if (models.Count == 0)
            {
                Debug.LogWarning($"[GlbImporterSetup] No .glb or .gltf files under {ModelRoot}.");
                return;
            }

            int changed = 0;
            foreach (string path in models)
            {
                // Skip what is already pointed at it, so re-running does not reimport the
                // whole folder — a glb is megabytes and reimport is not free.
                if (AssetDatabase.GetImporterOverride(path) == importer) continue;

                generic.Invoke(null, new object[] { path });
                changed++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[GlbImporterSetup] {changed} of {models.Count} model(s) switched to " +
                      $"{importer.Name}. Already set: {models.Count - changed}.");
        }

        static Type FindImporter()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(ImporterTypeName);
                if (found != null) return found;
            }
            return null;
        }

        static MethodInfo SetterFor(Type importer)
        {
            MethodInfo setter = typeof(AssetDatabase).GetMethod(
                "SetImporterOverride", new[] { typeof(string) });

            return setter == null ? null : setter.MakeGenericMethod(importer);
        }

        static List<string> Models()
        {
            var found = new List<string>();
            if (!Directory.Exists(ModelRoot)) return found;

            foreach (string file in Directory.GetFiles(ModelRoot, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".glb" && ext != ".gltf") continue;

                // Directory.GetFiles gives OS separators; the AssetDatabase wants forward ones.
                found.Add(file.Replace('\\', '/'));
            }
            return found;
        }
    }
}
