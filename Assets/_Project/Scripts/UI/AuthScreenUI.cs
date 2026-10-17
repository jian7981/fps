using UnityEngine;
using UnityEngine.UI;
using TMPro;      // TMP_InputField / TMP_Text

// 【阶段3-F】登录 / 注册界面（独立全屏界面，主流网游那种）
//
// 流程：
//   一进游戏 → 本地有令牌就自动登录（LobbyManager.Start 里跑 AuthService.AutoLogin）
//              ├ 令牌还有效 → 显示"已登录：昵称 Lv.x 经验 n" + 「进入游戏」
//              └ 没令牌 / 令牌已失效 → 显示登录注册区
//   点「进入游戏」 → 关掉本界面，显示大厅主菜单
//   点「退出登录」 → 清掉本地令牌，回到登录注册区（可继续登录或注册别的号）
//
// 挂载：大厅 Canvas（常驻物体）上 —— **不要挂在界面自己身上**
//（界面会被 SetActive(false)，挂在自己身上脚本就不跑了，RoomPanelUI 踩过这个坑）
public class AuthScreenUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyUI lobbyUI;               // 「进入游戏」时用它显示大厅主菜单
    public GameObject screen;             // 整个界面（全屏；保持激活，脚本按配置决定显隐）
    public GameObject loggedOutPanel;     // 未登录区：3 个输入框 + 登录/注册
    public GameObject loggedInPanel;      // 已登录区：资料行 + 进入游戏 + 退出登录
    public TMP_InputField usernameInput;
    public TMP_InputField passwordInput;
    public TMP_InputField nicknameInput;  // 注册时填；留空则用用户名
    public Button loginButton;
    public Button registerButton;
    public Button enterGameButton;        // 登录成功后才可点
    public Button logoutButton;
    public TMP_Text statusText;           // 未登录区的提示行
    public TMP_Text profileText;          // 已登录区的资料行

    private bool busy;                    // 请求进行中：防连点

    private void Start()
    {
        CheckRefs();

        if (screen == null) screen = gameObject;
        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.AddListener(OnRegisterClicked);
        if (enterGameButton != null) enterGameButton.onClick.AddListener(OnEnterGameClicked);
        if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClicked);

        // 密码框设成密码模式（显示成圆点），省得在 Inspector 里自己找
        if (passwordInput != null) passwordInput.contentType = TMP_InputField.ContentType.Password;

        AuthService.OnProfileChanged += RefreshState;

        // 没配账号服务（-lobbyserver）时整个账号功能是关闭的 → 界面不显示，也别盖住大厅
        if (!AuthService.HasService) Hide();
    }

    private void OnDestroy()
    {
        AuthService.OnProfileChanged -= RefreshState;
    }

    private void Update()
    {
        if (screen == null || !screen.activeSelf) return;
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // Esc：已登录 → 回大厅；未登录 → 什么都不做
        //（未登录时按 Esc 就能进大厅的话，这道登录门就白设了）
        if (AuthService.IsLoggedIn) OnEnterGameClicked();
    }

    public void Show()
    {
        screen.SetActive(true);
        if (usernameInput != null) usernameInput.ActivateInputField();   // 打开就能打字
        RefreshState();
    }

    public void Hide() => screen.SetActive(false);

    // ==================== 操作 ====================

    private void OnLoginClicked()
    {
        if (busy) return;

        string user = Trimmed(usernameInput);
        string password = Raw(passwordInput);
        if (user.Length == 0 || password.Length == 0) { SetStatus("请填写用户名和密码"); return; }

        SetBusy(true, "正在登录…");
        StartCoroutine(AuthService.Login(user, password,
            result =>
            {
                SetBusy(false);
                SetStatus("");
                StartCoroutine(AuthService.GetProfile(_ => { }));   // 顺手把等级/经验拉回来显示
            },
            err => { SetBusy(false); SetStatus(err); }));
    }

    private void OnRegisterClicked()
    {
        if (busy) return;

        string user = Trimmed(usernameInput);
        string password = Raw(passwordInput);
        string nickname = Trimmed(nicknameInput);

        if (user.Length == 0 || password.Length == 0) { SetStatus("请填写用户名和密码"); return; }
        if (password.Length < 6) { SetStatus("密码至少 6 位"); return; }
        if (nickname.Length == 0) nickname = user;      // 和后端规则一致：昵称留空就用用户名

        SetBusy(true, "正在注册…");
        StartCoroutine(AuthService.Register(user, password, nickname,
            result =>
            {
                SetBusy(false);
                SetStatus("");
                StartCoroutine(AuthService.GetProfile(_ => { }));
            },
            err => { SetBusy(false); SetStatus(err); }));
    }

    private void OnEnterGameClicked()
    {
        if (!AuthService.IsLoggedIn)
        {
            SetStatus("请先登录");
            return;
        }

        Hide();
        if (lobbyUI != null) lobbyUI.ShowMainPanel();
        else Debug.LogWarning("AuthScreenUI：lobbyUI 未赋值，点「进入游戏」显示不出大厅主菜单");
    }

    private void OnLogoutClicked()
    {
        if (busy) return;

        SetBusy(true, "正在登出…");
        StartCoroutine(AuthService.Logout());    // 本地令牌立刻清掉；后端吊销失败也不影响本机

        if (passwordInput != null) passwordInput.text = "";    // 清掉密码框，方便换个号登
        SetBusy(false);
        SetStatus("已登出，可以换个账号登录，或注册一个新账号");
    }

    // ==================== 状态刷新 ====================

    private void SetBusy(bool value, string message = null)
    {
        busy = value;
        RefreshState();
        if (message != null) SetStatus(message);
    }

    // 按登录态切换"未登录区 / 已登录区"，并刷新各控件可用性
    private void RefreshState()
    {
        bool loggedIn = AuthService.IsLoggedIn;
        bool ready = AuthService.HasService && !busy;

        if (loggedOutPanel != null) loggedOutPanel.SetActive(!loggedIn);
        if (loggedInPanel != null) loggedInPanel.SetActive(loggedIn);

        if (loginButton != null) loginButton.interactable = ready;
        if (registerButton != null) registerButton.interactable = ready;
        if (enterGameButton != null) enterGameButton.interactable = loggedIn;
        if (logoutButton != null) logoutButton.interactable = !busy;

        if (usernameInput != null) usernameInput.interactable = !busy && !loggedIn;
        if (passwordInput != null) passwordInput.interactable = !busy && !loggedIn;
        if (nicknameInput != null) nicknameInput.interactable = !busy && !loggedIn;

        if (profileText != null)
        {
            ProfileInfo profile = AuthService.CachedProfile;
            profileText.text = !loggedIn ? ""
                : (profile != null
                    ? AuthService.CurrentNickname + "　Lv." + profile.level + "　经验 " + profile.exp
                    : AuthService.CurrentNickname);
        }

        // 提示行：只在"空着"的时候补默认提示，别覆盖掉刚写上去的错误原因
        if (!AuthService.HasService) SetStatus("账号功能需要在 -lobbyserver 下使用");
        else if (!loggedIn && statusText != null && string.IsNullOrEmpty(statusText.text))
            SetStatus("请登录，或注册一个新账号");
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    // 用户名 / 昵称去掉首尾空格；密码不 Trim（有些密码真的带空格）
    private static string Trimmed(TMP_InputField field) => (field != null) ? field.text.Trim() : "";
    private static string Raw(TMP_InputField field) => (field != null) ? field.text : "";

    // 引用自检：缺哪个报哪个
    private void CheckRefs()
    {
        if (screen == null) Debug.LogWarning("AuthScreenUI：screen 未赋值（会用挂脚本的物体当界面根）");
        if (lobbyUI == null) Debug.LogError("AuthScreenUI：lobbyUI 未赋值（点「进入游戏」进不了大厅）");
        if (usernameInput == null) Debug.LogError("AuthScreenUI：usernameInput 未赋值");
        if (passwordInput == null) Debug.LogError("AuthScreenUI：passwordInput 未赋值");
        if (nicknameInput == null) Debug.LogWarning("AuthScreenUI：nicknameInput 未赋值（注册时昵称会用用户名）");
        if (loginButton == null) Debug.LogError("AuthScreenUI：loginButton 未赋值");
        if (registerButton == null) Debug.LogError("AuthScreenUI：registerButton 未赋值");
        if (enterGameButton == null) Debug.LogError("AuthScreenUI：enterGameButton 未赋值");
        if (statusText == null) Debug.LogError("AuthScreenUI：statusText 未赋值");
    }
}
