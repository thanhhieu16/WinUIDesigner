using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Shell
{
    /// <summary>
    /// The files most recently opened or saved under a new name, most recent first, kept in a
    /// <see cref="ShellSettings"/> file. Every change is saved at once. Recent-files spec
    /// Decision 2.
    /// </summary>
    public sealed class RecentFileList
    {
        public const int Capacity = 10;

        readonly string storePath;
        readonly List<string> items;

        public RecentFileList(string storePath)
        {
            this.storePath = storePath;
            items = ShellSettings.Load(storePath).RecentFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Capacity)
                .ToList();
        }

        public event EventHandler Changed;

        public IReadOnlyList<string> Items
        {
            get { return items; }
        }

        /// <summary>
        /// Puts <paramref name="fullPath"/> first, moving it up if it is already listed (compared
        /// case-insensitively, as ShellModel.Open's dedupe compares), and drops the oldest entry
        /// past <see cref="Capacity"/>.
        /// </summary>
        public void Add(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return;
            int existing = IndexOf(fullPath);
            if (existing == 0 && items[0] == fullPath)
                return;
            if (existing >= 0)
                items.RemoveAt(existing);
            items.Insert(0, fullPath);
            if (items.Count > Capacity)
                items.RemoveRange(Capacity, items.Count - Capacity);
            Commit();
        }

        public void Remove(string fullPath)
        {
            int existing = IndexOf(fullPath);
            if (existing < 0)
                return;
            items.RemoveAt(existing);
            Commit();
        }

        public void Clear()
        {
            if (items.Count == 0)
                return;
            items.Clear();
            Commit();
        }

        int IndexOf(string fullPath)
        {
            if (fullPath == null)
                return -1;
            return items.FindIndex(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Saves the whole list. Another running instance's changes are overwritten: the last
        /// change wins, and merging two windows' lists is out of scope.
        /// </summary>
        void Commit()
        {
            ShellSettings.Update(storePath, settings => settings.RecentFiles = new List<string>(items));
            var handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
