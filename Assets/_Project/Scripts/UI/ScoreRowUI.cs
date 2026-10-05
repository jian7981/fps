using UnityEngine;
using TMPro;

// ============ 战绩表的一行（名字 + 击杀 + 死亡 + 助攻）============
// 挂载：挂在 ScoreRow 预制体上
public class ScoreRowUI : MonoBehaviour
{
    [Header("四个格子")]
    public TMP_Text nameText;      // 名字
    public TMP_Text killsText;     // 击杀
    public TMP_Text deathsText;    // 死亡
    public TMP_Text assistsText;   // 助攻

    // 填入一个人的数据
    public void Set(string displayName, int kills, int deaths, int assists)
    {
        nameText.text = displayName;
        killsText.text = kills.ToString();
        deathsText.text = deaths.ToString();
        assistsText.text = assists.ToString();
    }

    // 空位（这局这个位置没放人）
    public void SetEmpty()
    {
        nameText.text = "空位";
        killsText.text = "-";
        deathsText.text = "-";
        assistsText.text = "-";
    }
}