using UnityEngine;
using TMPro;     // TMP_Text

// ============ 顶部比分条（表现层）============
// 订阅 MatchState 的比分事件，刷新"A队 n : n B队"和"先到 N 杀"提示
// 挂载：map1 的 MatchCanvas 上（注意：必须挂在"画布根"上，不能挂在会被隐藏的结算面板上）
public class MatchHUDUI : MonoBehaviour
{
    [Header("引用")]
    public MatchState matchState;    // 拖 GameManager 物体即可（Unity 会自动认它身上的 MatchState）
    public MatchRules matchRules;    // 同上，拖 GameManager 物体
    public TMP_Text scoreText;       // 大比分文字
    public TMP_Text hintText;        // "先到 X 杀获胜"

    private void Start()
    {
        if (matchState == null || scoreText == null)
        {
            Debug.LogError("MatchHUDUI：matchState / scoreText 未赋值");
            enabled = false;
            return;
        }

        // 提示文字由代码从规则里读，保证和 MatchRules 里的数字永远一致
        if (hintText != null && matchRules != null)
            hintText.text = "先到 " + matchRules.TargetKills + " 杀获胜";

        Refresh();
    }

    private void OnEnable()
    {
        if (matchState != null) matchState.OnScoreChanged += Refresh;
    }

    private void OnDisable()
    {
        if (matchState != null) matchState.OnScoreChanged -= Refresh;   // 配对取消订阅
    }

    // 比分变化：重画比分条
    private void Refresh()
    {
        if (scoreText == null || matchState == null) return;
        scoreText.text = "A队 " + matchState.TeamAScore + " : " + matchState.TeamBScore + " B队";
    }
}