using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RunRoute", menuName = "Game/Run Route")]
public class RunRouteData : ScriptableObject
{
    [System.Serializable]
    public class Chapter
    {
        public List<LevelData> normals = new List<LevelData>();
        public List<LevelData> elites = new List<LevelData>();
        public LevelData boss;
    }

    public List<Chapter> chapters = new List<Chapter>();

    public bool IsComplete
    {
        get
        {
            if (chapters.Count != 2) return false;
            foreach (Chapter chapter in chapters)
            {
                if (chapter == null || chapter.normals.Count != 6 || chapter.elites.Count != 2 || chapter.boss == null) return false;
                if (chapter.normals.Exists(level => level == null) || chapter.elites.Exists(level => level == null)) return false;
            }
            return true;
        }
    }
}
