using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// Registry for local skills (SkillDefinition) to be sent to the backend.
    /// Skills are scanned and loaded asynchronously on editor startup and domain reload.
    /// Callers of <see cref="GetSkills"/> implicitly wait for any in-progress scan before receiving results.
    /// </summary>
    static class SkillsRegistry
    {
        // Timeout, at which GetSkills() returns partial results
        const int k_GetSkillsTimeoutMs = 5000;

        static readonly object s_Lock = new(); // guards s_RegisteredSkills, s_SkillsCache, and s_BackgroundScanTask
        static readonly Dictionary<string, SkillDefinition> s_RegisteredSkills = new();
        // Cached read-only snapshot; null means dirty, rebuilt lazily in GetSnapshotLocked().
        static IReadOnlyDictionary<string, SkillDefinition> s_SkillsCache;

        // Written under s_Lock to keep the read-modify-write atomic; volatile so readers outside the lock
        // (IsLoadComplete, GetSkills, GetSkillsAsync) always see the latest value.
        static volatile Task s_BackgroundScanTask = Task.CompletedTask;

        /// <summary>
        /// True once the background skill scan has finished.
        /// </summary>
        internal static bool IsLoadComplete => s_BackgroundScanTask.IsCompleted;

        /// <summary>
        /// Called by SkillsScanner when a background scan starts, so <see cref="GetSkills"/> knows to wait for it.
        /// Replaces any previously registered task; callers are responsible for composing concurrent scans
        /// (e.g. <c>Task.WhenAll</c>) before registering. Superseded scans are already cancelled and their
        /// results discarded, so there is no value in continuing to wait for them.
        /// </summary>
        internal static void RegisterBackgroundScan(Task task)
        {
            lock (s_Lock)
                s_BackgroundScanTask = task ?? Task.CompletedTask;
        }

        // Rebuilds s_SkillsCache if dirty and returns it. Must be called with s_Lock held.
        static IReadOnlyDictionary<string, SkillDefinition> GetSnapshotLocked()
        {
            Debug.Assert(Monitor.IsEntered(s_Lock), "GetSnapshotLocked must be called with s_Lock held.");
            return s_SkillsCache ??= new ReadOnlyDictionary<string, SkillDefinition>(
                new Dictionary<string, SkillDefinition>(s_RegisteredSkills));
        }

        /// <summary>
        /// Returns all registered skills, waiting up to <c>k_GetSkillsTimeoutMs</c> for any in-progress scan.
        /// On timeout, returns partial results and logs a warning; <see cref="IsLoadComplete"/> stays false.
        /// </summary>
        public static IReadOnlyDictionary<string, SkillDefinition> GetSkills()
        {
            var task = s_BackgroundScanTask; // volatile read, atomic without s_Lock
            if (!task.IsCompleted)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var finished = task.Wait(k_GetSkillsTimeoutMs);
                    stopwatch.Stop();
                    if (finished)
                        InternalLog.Log($"[SkillsRegistry] GetSkills() waited {stopwatch.ElapsedMilliseconds}ms for background scan");
                    else
                        UnityEngine.Debug.LogWarning($"[SkillsRegistry] GetSkills() timed out after {stopwatch.ElapsedMilliseconds}ms - returning partial results. Check the skill folder(s) for slow or unreachable drives.");
                }
                catch (AggregateException ex)
                {
                    stopwatch.Stop();
                    UnityEngine.Debug.LogWarning($"[SkillsRegistry] GetSkills(): background scan faulted ({ex.InnerException?.Message}) - returning partial results.");
                }
            }

            lock (s_Lock)
                return GetSnapshotLocked();
        }

        /// <summary>
        /// Asynchronously waits for any in-progress scan and returns a snapshot of registered skills.
        /// Prefer over <see cref="GetSkills"/> when calling from an async context or the main thread.
        /// </summary>
        internal static async Task<IReadOnlyDictionary<string, SkillDefinition>> GetSkillsAsync()
        {
            var task = s_BackgroundScanTask; // volatile read, atomic without s_Lock
            if (!task.IsCompleted)
            {
                var winner = await Task.WhenAny(task, Task.Delay(k_GetSkillsTimeoutMs)).ConfigureAwait(false);
                if (winner != task)
                    UnityEngine.Debug.LogWarning($"[SkillsRegistry] GetSkillsAsync() timed out after {k_GetSkillsTimeoutMs}ms - returning partial results. Check the skill folder(s) for slow or unreachable drives.");
                else if (task.IsFaulted)
                    UnityEngine.Debug.LogWarning($"[SkillsRegistry] GetSkillsAsync(): background scan faulted ({task.Exception?.InnerException?.Message}) - returning partial results.");
                else
                    InternalLog.Log($"[SkillsRegistry] GetSkillsAsync() waited for background scan to complete");
            }

            lock (s_Lock)
                return GetSnapshotLocked();
        }

        /// <summary>
        /// Returns a snapshot of currently registered skills without waiting. See <see cref="IsLoadComplete"/>.
        /// </summary>
        internal static IReadOnlyDictionary<string, SkillDefinition> GetSkillsNoWait()
        {
            lock (s_Lock)
                return GetSnapshotLocked();
        }

        /// <summary>
        /// Registers a single skill. Skips silently if the name already exists. Thread-safe.
        /// </summary>
        public static void RegisterSkill(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrEmpty(skill.MetaData.Name))
                return;

            lock (s_Lock)
                AddSkillLocked(skill);
        }

        static void AddSkillLocked(SkillDefinition skill, List<SkillFileIssue> issues = null)
        {
            Debug.Assert(Monitor.IsEntered(s_Lock), "AddSkillLocked must be called with s_Lock held.");
            if (skill == null || string.IsNullOrEmpty(skill.MetaData.Name))
                return;

            var name = skill.MetaData.Name;
            if (s_RegisteredSkills.TryGetValue(name, out var existing))
            {
                issues?.Add(new SkillFileIssue(name, skill.Path,
                    $"A skill named '{name}' already exists at file location: {existing.Path}",
                    SkillFileIssue.ErrorLevel.Critical));
                InternalLog.LogWarning($"[SkillsRegistry] A skill with name '{name}' was skipped, a SkillDefinition with same name was already registered.");
                return;
            }
            
            s_RegisteredSkills[skill.MetaData.Name] = skill;
            s_SkillsCache = null;
        }

        /// <summary>
        /// Swaps skills by removing all with one kind of tag and adding skills. Thread-safe.
        /// </summary>
        internal static void ReplaceSkillsByTag(string tag, List<SkillDefinition> newSkills, List<SkillFileIssue> issues = null)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            lock (s_Lock)
            {
                RemoveByTag(tag);
                AddSkills(newSkills, issues);
            }
        }

        /// <summary>
        /// Removes all skills with the given tag. Thread-safe.
        /// </summary>
        public static void RemoveByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            lock (s_Lock)
            {
                var toRemove = s_RegisteredSkills
                    .Where(x => x.Value.Tags.Contains(tag))
                    .Select(x => x.Key)
                    .ToList();

                foreach (var key in toRemove)
                    s_RegisteredSkills.Remove(key);
                if (toRemove.Count > 0)
                    s_SkillsCache = null;
            }
        }

        /// <summary>
        /// Removes all registered skills. Thread-safe.
        /// </summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_RegisteredSkills.Clear();
                s_SkillsCache = null;
            }
        }

        /// <summary>
        /// Adds a batch of skills. Invalid or duplicate-named skills are skipped with a warning. Thread-safe.
        /// </summary>
        public static void AddSkills(List<SkillDefinition> skills, List<SkillFileIssue> issues = null)
        {
            if (skills == null || skills.Count == 0)
                return;

            lock (s_Lock)
            {
                foreach (var skill in skills)
                {
                    if (skill != null && skill.IsValid)
                        AddSkillLocked(skill, issues);
                    else
                        InternalLog.LogWarning("[SkillsRegistry] Skipped NULL, unnamed, or otherwise invalid skill when adding skills to registry. Look at any previous logs for failed SkillDefinition building steps.");
                }
            }
        }
    }
}
