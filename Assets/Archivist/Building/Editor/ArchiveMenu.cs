using System.IO;
using UnityEditor;
using UnityEngine;
using Archivist.Building.Collection;

namespace Archivist.Building.EditorTools
{
    /// <summary>
    /// Two menu items over the save file. Development tools, and the only ones that reach it
    /// from outside a running game.
    ///
    /// <para><b>Why a menu item as well as <c>Archive.resetOnLoad</c>.</b> The flag lives on a
    /// component, and <see cref="Archive"/> makes itself when a scene has none — so in that
    /// scene there is no checkbox to tick. Deleting the file does the same job and needs
    /// nothing in the scene at all. The flag is for "keep starting clean while I work"; this is
    /// for "throw that one away, now".</para>
    ///
    /// <para><b>It finds the archive if there is one</b>, so a scene that renamed its file is
    /// still the file that gets deleted. Falling back to the default rather than refusing,
    /// because a scene with no <see cref="Archive"/> still has a save — the component makes
    /// itself at run time and writes to the default name.</para>
    /// </summary>
    static class ArchiveMenu
    {
        const string DeleteItem = "Archivist/Save · Delete the save file";
        const string ShowItem   = "Archivist/Save · Show the save file";

        [MenuItem(DeleteItem, priority = 400)]
        static void Delete()
        {
            string path = SavePath();

            if (!File.Exists(path))
            {
                Debug.Log("[Archive] Nothing to delete — no save at " + path);
                return;
            }

            // Asked, because this is not undoable and a save is hours of somebody's shelving.
            if (!EditorUtility.DisplayDialog(
                    "Delete the save?",
                    "This removes\n\n" + path + "\n\nThe ledger, every binder and every loose "
                    + "sheet go with it. There is no undo.",
                    "Delete", "Keep it"))
                return;

            if (Archive.Discard(path)) Debug.Log("[Archive] deleted " + path);
        }

        [MenuItem(ShowItem, priority = 401)]
        static void Show()
        {
            string path = SavePath();

            if (File.Exists(path)) { EditorUtility.RevealInFinder(path); return; }

            Debug.Log("[Archive] No save at " + path + " — showing the folder instead.");
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        static string SavePath()
        {
            Archive archive = Object.FindFirstObjectByType<Archive>(FindObjectsInactive.Include);
            return archive != null ? archive.Path : Archive.PathOf(null);
        }
    }
}
