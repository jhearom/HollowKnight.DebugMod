using System;
using System.Collections;
using Modding;
using UnityEngine;
using static DebugMod.SaveState;

namespace DebugMod
{
    //Stored in separate class due to the amount of unique functionality
    internal static class PanthSaveState
    {
        private readonly struct PantheonSequenceDefinition
        {
            public PantheonSequenceDefinition(string resourceName, string playerDataKey)
            {
                ResourceName = resourceName;
                PlayerDataKey = playerDataKey;
            }

            public string ResourceName { get; }
            public string PlayerDataKey { get; }
        }

        private static readonly PantheonSequenceDefinition[] PantheonSequences =
        {
            new("Boss Sequence Tier 1", "bossDoorStateTier1"),
            new("Boss Sequence Tier 2", "bossDoorStateTier2"),
            new("Boss Sequence Tier 3", "bossDoorStateTier3"),
            new("Boss Sequence Tier 4", "bossDoorStateTier4"),
            new("Boss Sequence Tier 5", "bossDoorStateTier5"),
        };

        public static bool IsPantheonSequence(string sequenceName) => TryResolveSequence(sequenceName, out _);

        public static (string SequenceName, int BossIndex) SavePanthScene(string scene)
        {
            int BossIndex = BossSequenceController.BossIndex;
            BossSequence sequence = ReflectionHelper.GetField<BossSequence>(typeof(BossSequenceController), "currentSequence");
            string sequenceName = sequence != null ? (sequence.name ?? sequence.ToString()) : string.Empty;
            return (NormalizeSequenceName(sequenceName), BossIndex);
        }

        public static void LoadPanthScene(string SequenceName, int BossIndex)
        {
            if (!TryResolveSequence(SequenceName, out PantheonSequenceDefinition sequenceDefinition))
            {
                Console.AddLine("Pantheon restore could not resolve sequence: " + SequenceName);
                return;
            }

            BossSequenceController.Reset();

            BossSequence sequence = Resources.Load<BossSequence>($"GG/{sequenceDefinition.ResourceName}");
            if (sequence == null)
            {
                Console.AddLine("Pantheon restore could not load BossSequence resource: GG/" + sequenceDefinition.ResourceName);
                return;
            }

            BossSequenceController.SetupNewSequence(sequence, BossSequenceController.ChallengeBindings.None, sequenceDefinition.PlayerDataKey);
            ReflectionHelper.SetField<int>(typeof(BossSequenceController), "bossIndex", BossIndex);
            ReflectionHelper.CallMethod(typeof(BossSequenceController), "SetupBossScene");
            isPanthState = true;
        }

        public static IEnumerator SetupPanthTransition()
        {
            if (BossSequenceController.BossIndex == 0)
            {
                GameObject dreamEntry = GameObject.Find("Dream Entry");
                PlayMakerFSM entryFSM = dreamEntry.LocateMyFSM("Control");
                yield return new WaitUntil(() => entryFSM.ActiveStateName == "Start Fade");
                GameManager.instance.StartCoroutine(SetupLoadDelay(dreamEntry, entryFSM));
            }
            isPanthState = false;
        }

        public static IEnumerator SetupLoadDelay(GameObject dreamEntry, PlayMakerFSM entryFSM)
        {
            dreamEntry.SetActive(false);
            yield return new WaitForSeconds(DebugMod.settings.PanthLoadDelay);
            dreamEntry.SetActive(true);
            entryFSM.SetState("Start Fade");
        }

        private static bool TryResolveSequence(string sequenceName, out PantheonSequenceDefinition definition)
        {
            string normalized = NormalizeSequenceName(sequenceName);
            foreach (PantheonSequenceDefinition candidate in PantheonSequences)
            {
                if (candidate.ResourceName.Equals(normalized, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = default;
            return false;
        }

        private static string NormalizeSequenceName(string sequenceName)
        {
            if (string.IsNullOrEmpty(sequenceName))
            {
                return string.Empty;
            }

            const string BossSequenceSuffix = " (BossSequence)";
            return sequenceName.EndsWith(BossSequenceSuffix, StringComparison.Ordinal)
                ? sequenceName.Substring(0, sequenceName.Length - BossSequenceSuffix.Length)
                : sequenceName;
        }
    }
}
