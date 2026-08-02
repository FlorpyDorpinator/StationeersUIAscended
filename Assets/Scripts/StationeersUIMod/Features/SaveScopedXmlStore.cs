using System;
using System.IO;
using System.Xml.Serialization;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The one XmlSerializer funnel every small config store in <c>Features/</c> uses.
    ///
    /// Six stores (bag hotkeys, belt bindings, Grid collapse, Grid pins, hint usage, loadouts)
    /// had each grown their OWN private copy of the same twenty lines: build the folder path,
    /// <c>Directory.CreateDirectory</c>, <c>new XmlSerializer(typeof(T))</c>, open/create a
    /// <see cref="FileStream"/>, deserialize-or-warn, serialize-or-warn. That is the shape this
    /// class owns instead — the stores keep their own in-memory models, their own XML DTOs and
    /// their own log wording, and only the plumbing moved here.
    ///
    /// ON-DISK COMPATIBILITY IS THE POINT: this helper deliberately does NOT introduce settings,
    /// namespaces, encodings or writers of its own. It constructs a plain
    /// <c>new XmlSerializer(typeof(T))</c> and calls <c>Serialize(Stream, object)</c> on a
    /// <c>File.Create</c> stream — byte-for-byte the same call the six stores each made before —
    /// so every existing player file keeps its root element, its UTF-8 declaration and its
    /// indentation, and round-trips identically. Do not "improve" that here: a stray
    /// XmlWriterSettings or XmlSerializerNamespaces would silently rewrite everyone's config.
    ///
    /// PER-SAVE KEYING: the save-scoped stores all key their file off
    /// <see cref="BagProfileStore.CurrentSaveKey"/> (a filename-safe station name), living at
    /// <c>BepInEx/config/StationeersUIMod/&lt;folder&gt;/&lt;saveKey&gt;.xml</c>. The load path passes
    /// the key it just resolved; the save path passes its remembered <c>_loadedSaveKey</c>, which
    /// may be null before the first load — <see cref="FileFor"/> falls back to the current key for
    /// exactly that case (the <c>_loadedSaveKey ?? CurrentSaveKey()</c> idiom each store used).
    ///
    /// Every method is fail-soft: a missing file, a corrupt file or a locked directory yields
    /// null/false and at most one warning line. Nothing here touches game state, so nothing here
    /// has an MP dimension. No statics are held, so there is nothing to reset on hot reload.
    /// </summary>
    internal static class SaveScopedXmlStore
    {
        /// <summary>Config sub-folder for a store, e.g. "Hotkeys" -> <c>…/StationeersUIMod/Hotkeys</c>.</summary>
        public static string DirFor(string folder)
        {
            return Path.Combine(BagProfileStore.ConfigDir, folder);
        }

        /// <summary>Per-save file path for a store. A null/empty <paramref name="saveKey"/> resolves
        /// to the CURRENT save key — the <c>_loadedSaveKey ?? CurrentSaveKey()</c> save-path idiom.</summary>
        public static string FileFor(string folder, string saveKey)
        {
            string key = string.IsNullOrEmpty(saveKey) ? BagProfileStore.CurrentSaveKey() : saveKey;
            return Path.Combine(DirFor(folder), key + ".xml");
        }

        /// <summary>Deserialize <paramref name="path"/> into <typeparamref name="T"/>. Returns false
        /// with <paramref name="error"/> null when the file simply is not there (the normal "no
        /// state yet" case, which must not log), and false with a message when it exists but will
        /// not parse. Callers that want the standard warning use <see cref="Load{T}"/> instead;
        /// this overload exists for stores whose log wording differs (LoadoutStore names the file).</summary>
        public static bool TryLoad<T>(string path, out T value, out string error) where T : class
        {
            Exception ex;
            bool ok = TryLoad(path, out value, out ex);
            error = ex != null ? ex.Message : null;
            return ok;
        }

        /// <summary>As <see cref="TryLoad{T}(string,out T,out string)"/>, but hands back the
        /// EXCEPTION rather than just its message. Callers that react destructively to a bad file
        /// (quarantine) must be able to tell a genuine parse failure — <c>XmlException</c>, or the
        /// <c>InvalidOperationException</c> XmlSerializer wraps it in — from a transient
        /// <c>IOException</c>/<c>UnauthorizedAccessException</c> (a sync client or AV holding the
        /// file open for a moment). Renaming a healthy file because of a two-second lock is not
        /// fail-soft, it is data loss with extra steps.</summary>
        public static bool TryLoad<T>(string path, out T value, out Exception error) where T : class
        {
            value = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                var serializer = new XmlSerializer(typeof(T));
                using (var stream = File.OpenRead(path))
                    value = (T)serializer.Deserialize(stream);
                return value != null;
            }
            catch (Exception e)
            {
                value = null;
                error = e;
                return false;
            }
        }

        /// <summary>Is this a genuine "the XML is broken" failure (as opposed to a transient IO or
        /// permission problem)? <c>XmlSerializer.Deserialize</c> reports malformed documents as an
        /// <c>InvalidOperationException</c> wrapping an <c>XmlException</c>.</summary>
        public static bool IsParseFailure(Exception error)
        {
            return error is System.Xml.XmlException || error is InvalidOperationException;
        }

        /// <summary>Deserialize a file, or null if it is absent/corrupt. A corrupt file warns once as
        /// "&lt;label&gt; load failed: &lt;message&gt;" — the exact line each store logged before.</summary>
        public static T Load<T>(string path, string label) where T : class
        {
            T value;
            string error;
            if (TryLoad(path, out value, out error)) return value;
            if (error != null) UIALog.Warn(label + " load failed: " + error);
            return null;
        }

        /// <summary>Deserialize a store's per-save file, or null if absent/corrupt.</summary>
        public static T LoadPerSave<T>(string folder, string saveKey, string label) where T : class
        {
            return Load<T>(FileFor(folder, saveKey), label);
        }

        /// <summary>Serialize to <paramref name="path"/>, creating its folder first. Returns false
        /// (never throws) on any failure, warning as "&lt;label&gt; save failed: &lt;message&gt;".</summary>
        public static bool Save<T>(string path, T value, string label) where T : class
        {
            if (value == null || string.IsNullOrEmpty(path)) return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var serializer = new XmlSerializer(typeof(T));
                using (var stream = File.Create(path))
                    serializer.Serialize(stream, value);
                return true;
            }
            catch (Exception e)
            {
                UIALog.Warn(label + " save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Serialize a store's per-save file. See <see cref="FileFor"/> for key handling.</summary>
        public static bool SavePerSave<T>(string folder, string saveKey, T value, string label) where T : class
        {
            return Save(FileFor(folder, saveKey), value, label);
        }
    }
}
