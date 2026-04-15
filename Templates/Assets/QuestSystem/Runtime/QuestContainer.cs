using System.Collections.Generic;
using UnityEngine;

namespace PG.QuestSystem
{
    [CreateAssetMenu(menuName = "PG/Quest/Quest Container")]
    public class QuestContainer : ScriptableObject
    {
        public List<QuestData> quests = new List<QuestData>();

        public QuestData GetQuest(string questID)
        {
            foreach (var q in quests)
            {
                if (q != null && q.questID == questID)
                    return q;
            }
            return null;
        }
    }
}
